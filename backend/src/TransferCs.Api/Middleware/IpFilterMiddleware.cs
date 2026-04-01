using System.Net;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Middleware;

public class IpFilterMiddleware
{
  private readonly RequestDelegate _next;
  private readonly HashSet<string> _whitelist;
  private readonly HashSet<string> _blacklist;

  public IpFilterMiddleware(RequestDelegate next, IOptions<TransferCsOptions> options)
  {
    _next = next;
    TransferCsOptions config = options.Value;

    _whitelist = ParseList(config.IpWhitelist);
    _blacklist = ParseList(config.IpBlacklist);
  }

  public async Task InvokeAsync(HttpContext context)
  {
    string remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "";

    if (_whitelist.Count > 0)
      if (!_whitelist.Contains(remoteIp))
      {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Forbidden");
        return;
      }

    if (_blacklist.Count > 0 && _blacklist.Contains(remoteIp))
    {
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      await context.Response.WriteAsync("Forbidden");
      return;
    }

    await _next(context);
  }

  private static HashSet<string> ParseList(string list)
  {
    if (string.IsNullOrWhiteSpace(list))
      return [];

    return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
  }
}