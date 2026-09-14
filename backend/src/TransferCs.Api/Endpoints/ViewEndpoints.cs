using TransferCs.Api.Helpers;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class ViewEndpoints
{
  public static WebApplication MapViewEndpoints(this WebApplication app)
  {
    app.MapGet("/", HandleRoot);
    app.MapMethods("/admin/{token}/{filename}", ["GET", "HEAD"], HandleApplication);
    return app;
  }

  private static IResult HandleApplication(IWebHostEnvironment env)
  {
    string webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    return Results.File(Path.Combine(webRoot, "index.html"), "text/html");
  }

  private static IResult HandleRoot(HttpRequest request, IWebHostEnvironment env, SiteContext siteContext)
  {
    if (AcceptHelper.AcceptsHtml(request))
      return HandleApplication(env);

    string title = siteContext.Site.Options.Title;
    string baseUrl = UrlHelper.ResolveUrl(request, "", siteContext.Site.Options).TrimEnd('/');
    string usage = $"""
                    {title} - Easy file sharing from the command line

                    Usage:
                      Upload:    curl --upload-file ./hello.txt {baseUrl}/hello.txt
                      Download:  curl {baseUrl}/<token>/hello.txt -o hello.txt
                      Delete:    curl -X DELETE {baseUrl}/<token>/hello.txt/<deletion-token>

                    Options:
                      Max-Downloads: 1              Maximum number of downloads
                      File-Lifetime: 7d                   Expires in 7 days (supports: 1d12h, 30m, 3600s, or a date)

                    Examples:
                      curl --upload-file ./hello.txt {baseUrl}/hello.txt
                      curl -H "File-Lifetime: 7d" --upload-file ./hello.txt {baseUrl}/hello.txt
                      curl -H "File-Lifetime: 1d12h" --upload-file ./hello.txt {baseUrl}/hello.txt
                      curl -H "Max-Downloads: 1" --upload-file ./hello.txt {baseUrl}/hello.txt
                    """;

    return Results.Text(usage, "text/plain");
  }
}
