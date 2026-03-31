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
        context.Response.Headers["x-made-with"] = "<3 by DutchCoders";
        context.Response.Headers["x-served-by"] = "Proudly served by DutchCoders";
        context.Response.Headers["server"] = "Transfer.sh HTTP Server";
        await _next(context);
    }
}
