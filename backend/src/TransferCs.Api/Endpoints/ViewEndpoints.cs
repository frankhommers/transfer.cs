using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;

namespace TransferCs.Api.Endpoints;

public static class ViewEndpoints
{
  public static WebApplication MapViewEndpoints(this WebApplication app)
  {
    app.MapGet("/", HandleRoot);
    return app;
  }

  private static IResult HandleRoot(HttpRequest request, IWebHostEnvironment env, IOptions<TransferCsOptions> opts)
  {
    if (AcceptHelper.AcceptsHtml(request))
    {
      string indexPath = Path.Combine(env.WebRootPath ?? "wwwroot", "index.html");
      return Results.File(indexPath, "text/html");
    }

    string title = opts.Value.Title;
    string baseUrl = $"{request.Scheme}://{request.Host}";
    string usage = $"""
                    {title} - Easy file sharing from the command line

                    Usage:
                      Upload:    curl --upload-file ./hello.txt {baseUrl}/hello.txt
                      Download:  curl {baseUrl}/<token>/hello.txt -o hello.txt
                      Delete:    curl -X DELETE {baseUrl}/<token>/hello.txt/<deletion-token>

                    Options:
                      Max-Downloads: 1              Maximum number of downloads
                      Expires: 7d                   Expires in 7 days (supports: 1d12h, 30m, 3600s, or a date)

                    Examples:
                      curl --upload-file ./hello.txt {baseUrl}/hello.txt
                      curl -H "Expires: 7d" --upload-file ./hello.txt {baseUrl}/hello.txt
                      curl -H "Expires: 1d12h" --upload-file ./hello.txt {baseUrl}/hello.txt
                      curl -H "Max-Downloads: 1" --upload-file ./hello.txt {baseUrl}/hello.txt
                    """;

    return Results.Text(usage, "text/plain");
  }
}