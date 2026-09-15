using TransferCs.Api.Storage;

namespace TransferCs.Api.Helpers;

public sealed class BufferedUploadForm(Stream buffer, IFormCollection form) : IAsyncDisposable
{
  public IFormCollection Form { get; } = form;

  public static async Task<BufferedUploadForm> ReadAsync(HttpRequest request, DiskSpaceGuard diskSpace,
    CancellationToken ct)
  {
    Stream originalBody = request.Body;
    Stream buffer = diskSpace.CreateTemporaryFile("multipart");
    try
    {
      await originalBody.CopyToAsync(buffer, ct);
      buffer.Position = 0;
      // A seekable body lets ASP.NET reference sections without creating unguarded spill files.
      request.Body = buffer;
      IFormCollection form = await request.ReadFormAsync(ct);
      return new BufferedUploadForm(buffer, form);
    }
    catch
    {
      await buffer.DisposeAsync();
      throw;
    }
    finally
    {
      request.Body = originalBody;
    }
  }

  public ValueTask DisposeAsync() => buffer.DisposeAsync();
}
