namespace TransferCs.Api.Middleware;

public class LoveHeaderMiddleware
{
  private readonly RequestDelegate _next;

  public LoveHeaderMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    context.Response.Headers["x-made-with"] = "<3 inspired by transfer.sh";
    context.Response.Headers["x-served-by"] = "transfer.cs";
    context.Response.Headers["server"] = "transfer.cs";
    await _next(context);
  }
}