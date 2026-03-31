using TransferCs.Api.Configuration;

namespace TransferCs.Api.Helpers;

public static class UrlHelper
{
    public static string ResolveUrl(HttpRequest request, string path, TransferCsOptions options)
    {
        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault()
                     ?? request.Scheme;

        var host = request.Host.Host;
        var port = request.Host.Port;

        if (!string.IsNullOrEmpty(options.ProxyPort))
        {
            if (int.TryParse(options.ProxyPort, out var proxyPort))
                port = proxyPort;
        }

        var proxyPath = options.ProxyPath.TrimEnd('/');

        var portSuffix = port.HasValue && !IsDefaultPort(scheme, port.Value)
            ? $":{port.Value}"
            : "";

        var fullPath = string.IsNullOrEmpty(proxyPath)
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
