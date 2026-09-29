using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using TransferCs.Api.Configuration;
using TransferCs.Api.Models;
using TransferCs.Api.Services;
using TransferCs.Api.Tests.Helpers;

namespace TransferCs.Api.Tests.Services;

public class DownloadAuthorizerTests
{
  private readonly DownloadPasswordHasher _hasher = new(1_000);
  private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
  private readonly DownloadPasswordAttemptLimiter _limiter;
  private readonly DownloadAuthorizer _authorizer;
  private readonly FileMetadata _protected;

  public DownloadAuthorizerTests()
  {
    _limiter = new DownloadPasswordAttemptLimiter(_time);
    SiteContext siteContext = new();
    siteContext.Resolve(new ResolvedSite("default", [], "default", new TransferCsOptions
    {
      DownloadPasswordMaxAttempts = 2,
      DownloadPasswordAttemptWindowMinutes = 15,
      DownloadPasswordUnlockHours = 12
    }));
    _authorizer = new DownloadAuthorizer(_hasher, _limiter, siteContext, _time);
    _protected = new FileMetadata { PasswordHash = _hasher.Hash("secret") };
  }

  [Fact]
  public void UnprotectedFile_IsAllowedWithoutCredentials()
  {
    Assert.Equal(DownloadAuthorizationStatus.Allowed,
      _authorizer.Authorize(new DefaultHttpContext().Request, "token", "file", new FileMetadata()).Status);
  }

  [Fact]
  public void ProtectedFile_WithoutCredentials_IsMissingCredentials()
  {
    Assert.Equal(DownloadAuthorizationStatus.MissingCredentials,
      _authorizer.Authorize(new DefaultHttpContext().Request, "token", "file", _protected).Status);
  }

  [Fact]
  public void CorrectHeader_IsAllowedAndClearsFailures()
  {
    Assert.Equal(DownloadAuthorizationStatus.WrongPassword, Authorize(RequestWithPassword("wrong")).Status);

    Assert.Equal(DownloadAuthorizationStatus.Allowed, Authorize(RequestWithPassword("secret")).Status);
    Assert.Equal(DownloadAuthorizationStatus.WrongPassword, Authorize(RequestWithPassword("wrong")).Status);
    Assert.Equal(DownloadAuthorizationStatus.WrongPassword, Authorize(RequestWithPassword("wrong")).Status);
  }

  [Fact]
  public void WrongHeader_CountsTowardsLimit()
  {
    Assert.Equal(DownloadAuthorizationStatus.WrongPassword, Authorize(RequestWithPassword("wrong")).Status);
    Assert.Equal(DownloadAuthorizationStatus.WrongPassword, Authorize(RequestWithPassword("wrong")).Status);

    DownloadAuthorizationResult limited = Authorize(RequestWithPassword("secret"));

    Assert.Equal(DownloadAuthorizationStatus.TooManyAttempts, limited.Status);
    Assert.Equal(TimeSpan.FromMinutes(15), limited.RetryAfter);
  }

  [Fact]
  public void ValidCookie_IsAllowedEvenWhileLimited()
  {
    Authorize(RequestWithPassword("wrong"));
    Authorize(RequestWithPassword("wrong"));
    HttpRequest request = new DefaultHttpContext().Request;
    request.Headers.Cookie = $"{UnlockCookie.GetName("token", "file")}=" +
      UnlockCookie.Create(_protected.PasswordHash, "token", "file", _time.GetUtcNow().AddHours(1));

    Assert.Equal(DownloadAuthorizationStatus.Allowed, Authorize(request).Status);
  }

  [Fact]
  public void InvalidCookie_IsMissingCredentials()
  {
    HttpRequest request = new DefaultHttpContext().Request;
    request.Headers.Cookie = $"{UnlockCookie.GetName("token", "file")}=1.abc";

    Assert.Equal(DownloadAuthorizationStatus.MissingCredentials, Authorize(request).Status);
  }

  [Fact]
  public void VerifyPassword_SharesLimiterWithHeaderChecks()
  {
    Assert.Equal(DownloadAuthorizationStatus.WrongPassword,
      _authorizer.VerifyPassword("wrong", "token", "file", _protected).Status);
    Assert.Equal(DownloadAuthorizationStatus.WrongPassword, Authorize(RequestWithPassword("wrong")).Status);

    Assert.Equal(DownloadAuthorizationStatus.TooManyAttempts,
      _authorizer.VerifyPassword("secret", "token", "file", _protected).Status);
  }

  [Fact]
  public void ResetAttempts_ClearsLimitForThatFileOnly()
  {
    Authorize(RequestWithPassword("wrong"));
    Authorize(RequestWithPassword("wrong"));
    _authorizer.VerifyPassword("wrong", "other", "file", _protected);
    _authorizer.VerifyPassword("wrong", "other", "file", _protected);

    _authorizer.ResetAttempts("token", "file");

    Assert.Equal(DownloadAuthorizationStatus.Allowed, Authorize(RequestWithPassword("secret")).Status);
    Assert.Equal(DownloadAuthorizationStatus.TooManyAttempts,
      _authorizer.VerifyPassword("secret", "other", "file", _protected).Status);
  }

  [Theory]
  [InlineData("https", true)]
  [InlineData("http", false)]
  public void AppendUnlockCookie_SetsSecureAttributes(string scheme, bool secure)
  {
    DefaultHttpContext context = new();
    context.Request.Scheme = scheme;

    _authorizer.AppendUnlockCookie(context.Response, context.Request.IsHttps, "token", "file", _protected);

    SetCookieHeaderValue cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
    Assert.Equal(UnlockCookie.GetName("token", "file"), cookie.Name.ToString());
    Assert.True(cookie.HttpOnly);
    Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
    Assert.Equal("/", cookie.Path.ToString());
    Assert.Equal(secure, cookie.Secure);
    Assert.Equal(_time.GetUtcNow().AddHours(12), cookie.Expires);
    Assert.True(UnlockCookie.Validate(cookie.Value.ToString(), _protected.PasswordHash, "token", "file",
      _time.GetUtcNow()));
  }

  private DownloadAuthorizationResult Authorize(HttpRequest request) =>
    _authorizer.Authorize(request, "token", "file", _protected);

  private static HttpRequest RequestWithPassword(string password)
  {
    HttpRequest request = new DefaultHttpContext().Request;
    request.Headers[DownloadAuthorizer.HeaderName] = password;
    return request;
  }
}
