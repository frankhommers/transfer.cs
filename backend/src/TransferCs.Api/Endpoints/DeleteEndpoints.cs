using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Endpoints;

public static class DeleteEndpoints
{
  public static WebApplication MapDeleteEndpoints(this WebApplication app)
  {
    app.MapDelete("/{token}/{filename}/{deletionToken}", HandleDeleteAsync);
    return app;
  }

  private static async Task<IResult> HandleDeleteAsync(
    string token,
    string filename,
    string deletionToken,
    IStorageProvider storage,
    MetadataService metadataService,
    CancellationToken ct)
  {
    bool isValid = await metadataService.ValidateDeletionTokenAsync(token, filename, deletionToken, ct);
    if (!isValid)
      return Results.NotFound();

    try
    {
      await storage.DeleteAsync(token, filename, ct);
      await storage.DeleteAsync(token, $"{filename}.metadata", ct);
      return Results.Ok("File deleted");
    }
    catch (Exception ex) when (storage.IsNotExist(ex))
    {
      return Results.NotFound();
    }
  }
}