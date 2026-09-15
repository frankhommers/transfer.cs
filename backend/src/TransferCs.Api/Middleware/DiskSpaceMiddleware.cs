using TransferCs.Api.Storage;

namespace TransferCs.Api.Middleware;

public sealed class DiskSpaceMiddleware(RequestDelegate next, ILogger<DiskSpaceMiddleware> logger)
{
  public async Task InvokeAsync(HttpContext context)
  {
    try
    {
      await next(context);
    }
    catch (InsufficientStorageException exception) when (!context.Response.HasStarted)
    {
      logger.LogWarning("Disk space reserve reached for {StoragePath}", exception.StoragePath);
      context.Response.ContentLength = null;
      context.Response.StatusCode = StatusCodes.Status507InsufficientStorage;
      context.Response.ContentType = "text/plain; charset=utf-8";
      context.Response.Headers.CacheControl = "no-store";
      await context.Response.WriteAsync(exception.Message, context.RequestAborted);
    }
  }
}
