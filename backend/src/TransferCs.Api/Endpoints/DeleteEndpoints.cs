using TransferCs.Api.Services;

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
    MetadataService metadataService,
    CancellationToken ct)
  {
    if (!await metadataService.DeleteWithDeletionTokenAsync(token, filename, deletionToken, ct))
      return Results.NotFound();

    return Results.Text("File deleted");
  }
}
