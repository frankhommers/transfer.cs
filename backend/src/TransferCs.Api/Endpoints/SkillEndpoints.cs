using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;

namespace TransferCs.Api.Endpoints;

public static class SkillEndpoints
{
  private static string? _template;

  public static WebApplication MapSkillEndpoints(this WebApplication app)
  {
    app.MapGet("/SKILL.md", HandleSkillMd);
    return app;
  }

  private static IResult HandleSkillMd(
    HttpRequest request,
    IOptions<TransferCsOptions> optionsAccessor)
  {
    TransferCsOptions options = optionsAccessor.Value;
    string baseUrl = UrlHelper.ResolveUrl(request, "", options).TrimEnd('/');

    _template ??= LoadTemplate();

    string maxUploadSize = options.MaxUploadSizeKb > 0
      ? $"{options.MaxUploadSizeKb / 1024} MB"
      : "unlimited";

    string purgeDays = options.PurgeDays > 0
      ? $"{options.PurgeDays} days"
      : "disabled";

    string content = _template
      .Replace("{{Title}}", options.Title)
      .Replace("{{BaseUrl}}", baseUrl)
      .Replace("{{MaxUploadSize}}", maxUploadSize)
      .Replace("{{PurgeDays}}", purgeDays);

    return Results.Text(content, "text/markdown; charset=utf-8");
  }

  private static string LoadTemplate()
  {
    string templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "SKILL.md");
    return File.ReadAllText(templatePath);
  }
}
