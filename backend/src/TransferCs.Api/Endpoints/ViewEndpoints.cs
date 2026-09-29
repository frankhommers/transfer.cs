using System.Text;
using Microsoft.AspNetCore.StaticAssets;
using TransferCs.Api.Helpers;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class ViewEndpoints
{
  private const string HtmlContentType = "text/html; charset=utf-8";
  private const string IndexAssetPath = "index.html";

  public static WebApplication MapViewEndpoints(this WebApplication app)
  {
    app.MapGet("/", HandleRoot);
    app.MapMethods("/admin/{token}/{filename}", ["GET", "HEAD"], HandleApplication);
    app.MapMethods("/" + IndexAssetPath, ["GET", "HEAD"], HandleApplication);
    app.MapFallback(HandleFallback);
    return app;
  }

  public static StaticAssetsEndpointConventionBuilder WithSiteIndexHtml(
    this StaticAssetsEndpointConventionBuilder builder)
  {
    builder.Add(endpoint =>
    {
      if (endpoint.Metadata.OfType<StaticAssetDescriptor>().Any(IsIndexAsset))
        endpoint.RequestDelegate = HandleIndexAssetAsync;
    });
    return builder;
  }

  private static bool IsIndexAsset(StaticAssetDescriptor descriptor) =>
    descriptor.AssetPath == IndexAssetPath || descriptor.AssetPath.StartsWith(IndexAssetPath + ".");

  private static Task HandleIndexAssetAsync(HttpContext context) =>
    HandleApplication(context.Request, context.RequestServices.GetRequiredService<IndexHtmlProvider>(),
      context.RequestServices.GetRequiredService<SiteContext>()).ExecuteAsync(context);

  private static IResult HandleApplication(HttpRequest request, IndexHtmlProvider indexHtml, SiteContext siteContext)
  {
    string? html = indexHtml.Render(siteContext.Site.Options.Title);
    if (html == null)
      return Results.NotFound();
    if (!HttpMethods.IsHead(request.Method))
      return Results.Content(html, HtmlContentType);

    request.HttpContext.Response.ContentType = HtmlContentType;
    request.HttpContext.Response.ContentLength = Encoding.UTF8.GetByteCount(html);
    return Results.Empty;
  }

  private static IResult HandleFallback(HttpRequest request, IndexHtmlProvider indexHtml, SiteContext siteContext) =>
    HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
      ? HandleApplication(request, indexHtml, siteContext)
      : Results.NotFound();

  private static IResult HandleRoot(HttpRequest request, IndexHtmlProvider indexHtml, SiteContext siteContext)
  {
    if (AcceptHelper.AcceptsHtml(request))
      return HandleApplication(request, indexHtml, siteContext);

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
