using TransferCs.Api.Helpers;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class SkillEndpoints
{
  private static string? _skillTemplate;
  private static string? _installScript;
  private static string? _transferScript;

  public static WebApplication MapSkillEndpoints(this WebApplication app)
  {
    app.MapGet("/SKILL.md", HandleSkillMd);
    app.MapGet("/install.sh", HandleInstallScript);
    app.MapGet("/transfer.sh", HandleTransferScript);
    return app;
  }

  private static IResult HandleSkillMd(
    HttpRequest request,
    SiteContext siteContext)
  {
    Configuration.TransferCsOptions options = siteContext.Site.Options;
    string baseUrl = UrlHelper.ResolveUrl(request, "", options).TrimEnd('/');

    _skillTemplate ??= LoadTemplate("SKILL.md");

    string maxUploadSize = options.MaxUploadSizeKb > 0
      ? $"{options.MaxUploadSizeKb / 1024} MB"
      : "unlimited";

    string purgeDays = options.PurgeDays > 0
      ? $"{options.PurgeDays} days"
      : "disabled";

    string content = _skillTemplate
      .Replace("{{Title}}", options.Title)
      .Replace("{{BaseUrl}}", baseUrl)
      .Replace("{{MaxUploadSize}}", maxUploadSize)
      .Replace("{{PurgeDays}}", purgeDays);

    return Results.Text(content, "text/markdown; charset=utf-8");
  }

  private static IResult HandleInstallScript(
    HttpRequest request,
    SiteContext siteContext)
  {
    Configuration.TransferCsOptions options = siteContext.Site.Options;
    string baseUrl = UrlHelper.ResolveUrl(request, "", options).TrimEnd('/');

    _installScript ??= LoadTemplate("install.sh");

    string content = _installScript.Replace("__BASE_URL__", baseUrl);
    return Results.Text(content, "text/plain; charset=utf-8");
  }

  private static IResult HandleTransferScript(
    HttpRequest request,
    SiteContext siteContext)
  {
    Configuration.TransferCsOptions options = siteContext.Site.Options;
    string baseUrl = UrlHelper.ResolveUrl(request, "", options).TrimEnd('/');

    _transferScript ??= LoadTemplate("transfer");

    string content = _transferScript.Replace("__BASE_URL__", baseUrl);
    return Results.Text(content, "text/plain; charset=utf-8");
  }

  private static string LoadTemplate(string name)
  {
    string templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", name);
    return File.ReadAllText(templatePath);
  }
}
