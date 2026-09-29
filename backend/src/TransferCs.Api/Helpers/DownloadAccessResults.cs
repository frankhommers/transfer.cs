using System.Globalization;
using TransferCs.Api.Services;

namespace TransferCs.Api.Helpers;

public static class DownloadAccessResults
{
  public static IResult Denied(HttpResponse response, DownloadAuthorizationResult result)
  {
    response.Headers.CacheControl = "no-store";
    if (result.Status == DownloadAuthorizationStatus.TooManyAttempts)
    {
      long seconds = (long)Math.Ceiling(result.RetryAfter.TotalSeconds);
      response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
      return Results.Text($"Too many incorrect passwords for this file. Try again in {seconds} seconds.\n",
        "text/plain", statusCode: StatusCodes.Status429TooManyRequests);
    }

    string message = result.Status == DownloadAuthorizationStatus.WrongPassword
      ? "Incorrect password.\n"
      : $"This file is password protected. Send the password in the {DownloadAuthorizer.HeaderName} header.\n";
    return Results.Text(message, "text/plain", statusCode: StatusCodes.Status401Unauthorized);
  }

  /// <summary>
  /// Browsers without credentials land on the share page, which shows the unlock form; action routes
  /// redirect there so a shared /download link still leads somewhere useful.
  /// </summary>
  public static IResult DeniedDownload(HttpRequest request, DownloadAuthorizationResult result, string? action,
    string token, string filename)
  {
    if (result.Status != DownloadAuthorizationStatus.MissingCredentials || !AcceptHelper.AcceptsHtml(request))
      return Denied(request.HttpContext.Response, result);

    IServiceProvider services = request.HttpContext.RequestServices;
    SiteContext siteContext = services.GetRequiredService<SiteContext>();
    request.HttpContext.Response.Headers.CacheControl = "no-store";
    if (action == null)
      return Endpoints.ViewEndpoints.HandleApplication(request, services.GetRequiredService<IndexHtmlProvider>(),
        siteContext);

    request.HttpContext.Response.Headers.Location = UrlHelper.ResolveUrl(request,
      $"/{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(filename)}", siteContext.Site.Options);
    return Results.StatusCode(StatusCodes.Status303SeeOther);
  }
}
