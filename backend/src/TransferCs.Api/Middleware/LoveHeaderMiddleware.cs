using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
using TransferCs.Api.Services;

namespace TransferCs.Api.Middleware;

public class LoveHeaderMiddleware
{
  private readonly RequestDelegate _next;
  private readonly string _defaultTitle;

  public LoveHeaderMiddleware(RequestDelegate next, IOptions<TransferCsOptions> options)
  {
    _next = next;
    _defaultTitle = options.Value.Title;
  }

  public async Task InvokeAsync(HttpContext context, SiteContext siteContext)
  {
    string servedBy = HeaderValueHelper.ToSafeValue(
      siteContext.IsResolved ? siteContext.Site.Options.Title : _defaultTitle);
    context.Response.Headers["x-made-with"] = "<3 inspired by transfer.sh";
    if (servedBy.Length > 0)
    {
      context.Response.Headers["x-served-by"] = servedBy;
      context.Response.Headers["server"] = servedBy;
    }
    await _next(context);
  }
}
