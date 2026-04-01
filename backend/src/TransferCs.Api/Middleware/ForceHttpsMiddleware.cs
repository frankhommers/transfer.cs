using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Middleware;

public class ForceHttpsMiddleware
{
  private readonly RequestDelegate _next;
  private readonly bool _forceHttps;

  public ForceHttpsMiddleware(RequestDelegate next, IOptions<TransferCsOptions> options)
  {
    _next = next;
    _forceHttps = options.Value.ForceHttps;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    if (!_forceHttps)
    {
      await _next(context);
      return;
    }

    HttpRequest request = context.Request;

    // Skip health check
    if (request.Path.StartsWithSegments("/health"))
    {
      await _next(context);
      return;
    }

    // Skip if already HTTPS
    if (request.IsHttps)
    {
      await _next(context);
      return;
    }

    // Skip if behind HTTPS proxy
    if (string.Equals(request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https",
          StringComparison.OrdinalIgnoreCase))
    {
      await _next(context);
      return;
    }

    // Skip .onion hosts
    string host = request.Host.Host;
    if (host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase))
    {
      await _next(context);
      return;
    }

    // Redirect to HTTPS with 308 Permanent Redirect
    string httpsUrl = $"https://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
    context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
    context.Response.Headers.Location = httpsUrl;
  }
}