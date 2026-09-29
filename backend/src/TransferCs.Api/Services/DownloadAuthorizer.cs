using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
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

    if (!DownloadPasswordHeader.IsPresent(request.Headers))
      return DownloadAuthorizationResult.MissingCredentials;
    return DownloadPasswordHeader.Read(request.Headers, out string? password) == null && password != null
      ? VerifyPassword(password, token, filename, metadata)
      : DownloadAuthorizationResult.WrongPassword;
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

    ResetAttempts(token, filename);
    return DownloadAuthorizationResult.Allowed;
  }

  public void ResetAttempts(string token, string filename) =>
    limiter.Reset(siteContext.Site.Id, token, filename);

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
