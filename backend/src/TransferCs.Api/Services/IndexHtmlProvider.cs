using System.Net;
using System.Text.RegularExpressions;

namespace TransferCs.Api.Services;

public sealed partial class IndexHtmlProvider
{
  private readonly string _path;
  private string[]? _parts;

  public IndexHtmlProvider(IWebHostEnvironment environment)
  {
    string webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    _path = Path.Combine(webRoot, "index.html");
  }

  public string? Render(string title)
  {
    string[]? parts = _parts ??= Load();
    if (parts == null)
      return null;
    return parts.Length == 1 ? parts[0] : string.Concat(parts[0], WebUtility.HtmlEncode(title), parts[1]);
  }

  private string[]? Load()
  {
    if (!File.Exists(_path))
      return null;

    string html = File.ReadAllText(_path);
    Group content = TitlePattern().Match(html).Groups["content"];
    return content.Success ? [html[..content.Index], html[(content.Index + content.Length)..]] : [html];
  }

  [GeneratedRegex(@"<title\b[^>]*>(?<content>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex TitlePattern();
}
