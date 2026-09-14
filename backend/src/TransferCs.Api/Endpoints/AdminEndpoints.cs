using System.Net.Http.Headers;
using TransferCs.Api.Models;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class AdminEndpoints
{
  public static WebApplication MapAdminEndpoints(this WebApplication app)
  {
    app.MapGet("/api/admin/{token}/{filename}", HandleMetadataAsync);
    app.MapDelete("/api/admin/{token}/{filename}", HandleDeleteAsync);
    return app;
  }

  private static string ReadBearerToken(HttpRequest request) =>
    AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out AuthenticationHeaderValue? auth) &&
    auth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
      ? auth.Parameter ?? "" : "";

  private static async Task<IResult> HandleMetadataAsync(
    string token,
    string filename,
    HttpRequest request,
    MetadataService metadataService,
    CancellationToken ct)
  {
    string adminToken = ReadBearerToken(request);
    FileMetadata? metadata = await metadataService.LoadForAdminAsync(token, filename, adminToken, ct);
    if (metadata == null)
      return Results.NotFound();

    return Results.Json(new AdminMetadata
    {
      Filename = filename,
      ContentLength = metadata.ContentLength,
      ContentType = metadata.ContentType,
      Sha256 = metadata.Sha256,
      Downloads = metadata.Downloads,
      MaxDownloads = metadata.MaxDownloads,
      MaxDate = metadata.MaxDate,
      DownloadLogTotal = metadata.DownloadLogTotal,
      DownloadLog = metadata.DownloadLog
    }, AppJsonContext.Default.AdminMetadata);
  }

  private static async Task<IResult> HandleDeleteAsync(
    string token,
    string filename,
    HttpRequest request,
    MetadataService metadataService,
    CancellationToken ct)
  {
    string adminToken = ReadBearerToken(request);
    if (!await metadataService.DeleteForAdminAsync(token, filename, adminToken, ct))
      return Results.NotFound();

    return Results.Text("File deleted");
  }
}
