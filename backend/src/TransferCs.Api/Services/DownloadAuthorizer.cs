using Microsoft.Extensions.Primitives;
using TransferCs.Api.Configuration;
using TransferCs.Api.Models;

namespace TransferCs.Api.Services;

public sealed class DownloadAuthorizer(
  DownloadPasswordHasher hasher,
  DownloadPasswordAttemptLimiter limiter,
  SiteContext siteContext,
  TimeProvider timeProvider)
{
  public const string HeaderName = "Download-Password";

  public DownloadAuthorizationResult Authorize(HttpRequest request, string token, string filename,
    FileMetadata metadata)
  {
    if (!metadata.PasswordProtected)
      return DownloadAuthorizationResult.Allowed;

    // Cookie first: it is a cheap HMAC check, so range requests and resumed downloads never hit PBKDF2.
    if (UnlockCookie.Validate(request.Cookies[UnlockCookie.GetName(token, filename)], metadata.PasswordHash,
          token, filename, timeProvider.GetUtcNow()))
      return DownloadAuthorizationResult.Allowed;

    return request.Headers.TryGetValue(HeaderName, out StringValues password)
      ? VerifyPassword(password.ToString(), token, filename, metadata)
      : DownloadAuthorizationResult.MissingCredentials;
  }

  public DownloadAuthorizationResult VerifyPassword(string password, string token, string filename,
    FileMetadata metadata)
  {
    if (!metadata.PasswordProtected)
      return DownloadAuthorizationResult.Allowed;

    TransferCsOptions options = siteContext.Site.Options;
    if (!limiter.TryBeginAttempt(siteContext.Site.Id, token, filename, options.DownloadPasswordMaxAttempts,
          TimeSpan.FromMinutes(options.DownloadPasswordAttemptWindowMinutes), out TimeSpan retryAfter))
      return new DownloadAuthorizationResult(DownloadAuthorizationStatus.TooManyAttempts, retryAfter);

    if (!hasher.Verify(password, metadata.PasswordHash))
      return DownloadAuthorizationResult.WrongPassword;

    limiter.Reset(siteContext.Site.Id, token, filename);
    return DownloadAuthorizationResult.Allowed;
  }

  public void AppendUnlockCookie(HttpResponse response, bool secure, string token, string filename,
    FileMetadata metadata)
  {
    DateTimeOffset expiry = timeProvider.GetUtcNow()
      .AddHours(siteContext.Site.Options.DownloadPasswordUnlockHours);
    response.Cookies.Append(UnlockCookie.GetName(token, filename),
      UnlockCookie.Create(metadata.PasswordHash, token, filename, expiry), new CookieOptions
      {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Secure = secure,
        Expires = expiry
      });
  }
}
