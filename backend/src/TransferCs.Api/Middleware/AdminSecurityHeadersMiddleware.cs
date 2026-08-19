namespace TransferCs.Api.Middleware;

public class AdminSecurityHeadersMiddleware
{
  private readonly RequestDelegate _next;

  public AdminSecurityHeadersMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    if (context.Request.Path.StartsWithSegments("/admin") ||
        context.Request.Path.StartsWithSegments("/api/admin"))
    {
      context.Response.Headers.CacheControl = "no-store";
      context.Response.Headers["Referrer-Policy"] = "no-referrer";
      context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    await _next(context);
  }
}
