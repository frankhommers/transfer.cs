using System.Text;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
using TransferCs.Api.Services;

namespace TransferCs.Api.Middleware;

public class BasicAuthMiddleware
{
  private readonly RequestDelegate _next;
  private readonly TransferCsOptions _options;
  private readonly HtpasswdService? _htpasswdService;
  private readonly IpListMatcher _authIpWhitelist;

  public BasicAuthMiddleware(RequestDelegate next, IOptions<TransferCsOptions> options)
  {
    _next = next;
    _options = options.Value;

    if (!string.IsNullOrEmpty(_options.HttpAuthHtpasswd))
      _htpasswdService = new HtpasswdService(_options.HttpAuthHtpasswd);

    _authIpWhitelist = new IpListMatcher(_options.HttpAuthIpWhitelist);
  }

  public async Task InvokeAsync(HttpContext context)
  {
    // Admin endpoints authenticate with the per-file Admin-Token header. Requiring global
    // basic auth as well would turn a missing admin token into 401 instead of the intended
    // non-enumerable 404 response.
    if (context.Request.Path.StartsWithSegments("/api/admin"))
    {
      await _next(context);
      return;
    }

    // Only protect PUT/POST/DELETE methods
    string method = context.Request.Method;
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
    string remoteIp = ClientIpHelper.Get(context);
    if (_authIpWhitelist.Matches(remoteIp))
    {
      await _next(context);
      return;
    }

    // Parse Basic auth header
    string? authHeader = context.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
      ReturnUnauthorized(context);
      return;
    }

    try
    {
      string encoded = authHeader["Basic ".Length..].Trim();
      string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
      int colonIndex = decoded.IndexOf(':');
      if (colonIndex < 0)
      {
        ReturnUnauthorized(context);
        return;
      }

      string username = decoded[..colonIndex];
      string password = decoded[(colonIndex + 1)..];

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
}
