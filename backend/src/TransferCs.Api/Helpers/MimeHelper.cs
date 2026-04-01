using Microsoft.AspNetCore.StaticFiles;

namespace TransferCs.Api.Helpers;

public static class MimeHelper
{
  private static readonly FileExtensionContentTypeProvider Provider;

  static MimeHelper()
  {
    Provider = new FileExtensionContentTypeProvider();
    Provider.Mappings[".md"] = "text/x-markdown";
  }

  public static string GetMimeType(string filename)
  {
    if (Provider.TryGetContentType(filename, out string? contentType))
      return contentType;
    return "application/octet-stream";
  }
}