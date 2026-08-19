using TransferCs.Api.Services;

namespace TransferCs.Api.Middleware;

public sealed class SiteResolutionMiddleware
{
  private readonly RequestDelegate _next;

  public SiteResolutionMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context, SiteResolver resolver, SiteContext siteContext)
  {
    if (context.Request.Path == "/health")
    {
      await _next(context);
      return;
    }

    ResolvedSite? site = resolver.Resolve(context.Request.Host.Host);
    if (site == null)
    {
      context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
      await context.Response.WriteAsync("Host is not configured.");
      return;
    }

    siteContext.Resolve(site);
    await _next(context);
  }
}
