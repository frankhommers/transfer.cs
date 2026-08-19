using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;

namespace TransferCs.Api.Middleware;

public class IpFilterMiddleware
{
  private readonly RequestDelegate _next;
  private readonly IpListMatcher _whitelist;
  private readonly IpListMatcher _blacklist;

  public IpFilterMiddleware(RequestDelegate next, IOptions<TransferCsOptions> options)
  {
    _next = next;
    TransferCsOptions config = options.Value;

    _whitelist = new IpListMatcher(config.IpWhitelist);
    _blacklist = new IpListMatcher(config.IpBlacklist);
  }

  public async Task InvokeAsync(HttpContext context)
  {
    string remoteIp = ClientIpHelper.Get(context);

    if (!_whitelist.IsEmpty && !_whitelist.Matches(remoteIp))
    {
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      await context.Response.WriteAsync("Forbidden");
      return;
    }

    if (_blacklist.Matches(remoteIp))
    {
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      await context.Response.WriteAsync("Forbidden");
      return;
    }

    await _next(context);
  }
}
