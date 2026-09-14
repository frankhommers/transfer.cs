using System.Net.Http.Headers;

namespace TransferCs.Api.Helpers;

public static class AcceptHelper
{
  public static bool PrefersJson(HttpRequest request)
  {
    double jsonQuality = -1;
    double textQuality = -1;
    foreach (string value in request.Headers.Accept.ToString().Split(','))
    {
      if (!MediaTypeWithQualityHeaderValue.TryParse(value, out MediaTypeWithQualityHeaderValue? mediaType))
      {
        continue;
      }

      if (string.Equals(mediaType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
      {
        jsonQuality = Math.Max(jsonQuality, mediaType.Quality ?? 1);
      }
      else if (string.Equals(mediaType.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase))
      {
        textQuality = Math.Max(textQuality, mediaType.Quality ?? 1);
      }
    }

    return jsonQuality > 0 && jsonQuality >= textQuality;
  }

  public static bool AcceptsHtml(HttpRequest request)
  {
    string? accept = request.Headers.Accept.FirstOrDefault();
    if (string.IsNullOrEmpty(accept))
      return false;

    return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
  }
}
