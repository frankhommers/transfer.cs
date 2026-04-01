using TransferCs.Api.Configuration;

namespace TransferCs.Api.Helpers;

public static class UrlHelper
{
  public static string ResolveUrl(HttpRequest request, string path, TransferCsOptions options)
  {
    if (!string.IsNullOrEmpty(options.BaseUrl))
      return $"{options.BaseUrl.TrimEnd('/')}{path}";

    string scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault()
                    ?? request.Scheme;

    string host = request.Host.Host;
    int? port = request.Host.Port;

    if (!string.IsNullOrEmpty(options.ProxyPort))
      if (int.TryParse(options.ProxyPort, out int proxyPort))
        port = proxyPort;

    string proxyPath = options.ProxyPath.TrimEnd('/');

    string portSuffix = port.HasValue && !IsDefaultPort(scheme, port.Value)
      ? $":{port.Value}"
      : "";

    string fullPath = string.IsNullOrEmpty(proxyPath)
      ? path
      : $"{proxyPath}{path}";

    return $"{scheme}://{host}{portSuffix}{fullPath}";
  }

  public static string WebAddress(HttpRequest request, TransferCsOptions options)
  {
    return ResolveUrl(request, "/", options);
  }

  private static bool IsDefaultPort(string scheme, int port)
  {
    return (scheme == "http" && port == 80) || (scheme == "https" && port == 443);
  }
}