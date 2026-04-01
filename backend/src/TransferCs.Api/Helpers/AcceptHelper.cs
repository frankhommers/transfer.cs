namespace TransferCs.Api.Helpers;

public static class AcceptHelper
{
  public static bool AcceptsHtml(HttpRequest request)
  {
    string? accept = request.Headers.Accept.FirstOrDefault();
    if (string.IsNullOrEmpty(accept))
      return false;

    return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
  }
}