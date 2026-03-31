using System.Text;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;

namespace TransferCs.Api.Middleware;

public class BasicAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TransferCsOptions _options;
    private readonly HtpasswdService? _htpasswdService;
    private readonly HashSet<string> _authIpWhitelist;

    public BasicAuthMiddleware(RequestDelegate next, IOptions<TransferCsOptions> options)
    {
        _next = next;
        _options = options.Value;

        if (!string.IsNullOrEmpty(_options.HttpAuthHtpasswd))
            _htpasswdService = new HtpasswdService(_options.HttpAuthHtpasswd);

        _authIpWhitelist = ParseList(_options.HttpAuthIpWhitelist);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only protect PUT/POST/DELETE methods
        var method = context.Request.Method;
        if (method != "PUT" && method != "POST" && method != "DELETE")
        {
            await _next(context);
            return;
        }

        // Skip if no auth configured
        if (string.IsNullOrEmpty(_options.HttpAuthUser) &&
            string.IsNullOrEmpty(_options.HttpAuthHtpasswd))
        {
            await _next(context);
            return;
        }

        // Check IP whitelist for auth bypass
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "";
        if (_authIpWhitelist.Count > 0 && _authIpWhitelist.Contains(remoteIp))
        {
            await _next(context);
            return;
        }

        // Parse Basic auth header
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            ReturnUnauthorized(context);
            return;
        }

        try
        {
            var encoded = authHeader["Basic ".Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var colonIndex = decoded.IndexOf(':');
            if (colonIndex < 0)
            {
                ReturnUnauthorized(context);
                return;
            }

            var username = decoded[..colonIndex];
            var password = decoded[(colonIndex + 1)..];

            // Validate against config user/pass
            if (!string.IsNullOrEmpty(_options.HttpAuthUser) &&
                username == _options.HttpAuthUser &&
                password == _options.HttpAuthPass)
            {
                await _next(context);
                return;
            }

            // Validate against htpasswd
            if (_htpasswdService != null && _htpasswdService.Validate(username, password))
            {
                await _next(context);
                return;
            }
        }
        catch (FormatException)
        {
            // Invalid base64
        }

        ReturnUnauthorized(context);
    }

    private static void ReturnUnauthorized(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"transfer.sh\"";
    }

    private static HashSet<string> ParseList(string list)
    {
        if (string.IsNullOrWhiteSpace(list))
            return new HashSet<string>();

        return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
