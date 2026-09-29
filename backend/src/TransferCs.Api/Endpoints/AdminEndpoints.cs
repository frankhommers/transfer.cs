using System.Net.Http.Headers;
using System.Text.Json;
using TransferCs.Api.Helpers;
using TransferCs.Api.Models;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class AdminEndpoints
{
  public static WebApplication MapAdminEndpoints(this WebApplication app)
  {
    app.MapGet("/api/admin/{token}/{filename}", HandleMetadataAsync);
    app.MapDelete("/api/admin/{token}/{filename}", HandleDeleteAsync);
    app.MapPut("/api/admin/{token}/{filename}/password", HandleSetPasswordAsync);
    app.MapDelete("/api/admin/{token}/{filename}/password", HandleRemovePasswordAsync);
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
      PasswordProtected = metadata.PasswordProtected,
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

  private static async Task<IResult> HandleSetPasswordAsync(
    string token,
    string filename,
    HttpRequest request,
    MetadataService metadataService,
    DownloadPasswordHasher hasher,
    DownloadAuthorizer authorizer,
    CancellationToken ct)
  {
    // Authorize before reading the body or hashing so unauthenticated callers learn nothing and cost no PBKDF2 work.
    string adminToken = ReadBearerToken(request);
    if (!await metadataService.ValidateAdminTokenAsync(token, filename, adminToken, ct))
      return Results.NotFound();
    if (!request.HasJsonContentType())
      return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

    SetPasswordRequest? body;
    try
    {
      body = await JsonSerializer.DeserializeAsync(request.Body, AppJsonContext.Default.SetPasswordRequest, ct);
    }
    catch (JsonException)
    {
      return Results.BadRequest("Invalid JSON body.");
    }

    if (body?.Password is not { Length: > 0 and <= DownloadPasswordHeader.MaxLength } password)
      return Results.BadRequest($"The password must be 1 to {DownloadPasswordHeader.MaxLength} characters.");

    return await UpdatePasswordHashAsync(token, filename, adminToken, hasher.Hash(password), metadataService,
      authorizer, ct);
  }

  private static async Task<IResult> HandleRemovePasswordAsync(
    string token,
    string filename,
    HttpRequest request,
    MetadataService metadataService,
    DownloadAuthorizer authorizer,
    CancellationToken ct) =>
    await UpdatePasswordHashAsync(token, filename, ReadBearerToken(request), "", metadataService, authorizer, ct);

  private static async Task<IResult> UpdatePasswordHashAsync(string token, string filename, string adminToken,
    string passwordHash, MetadataService metadataService, DownloadAuthorizer authorizer, CancellationToken ct)
  {
    if (!await metadataService.SetPasswordHashForAdminAsync(token, filename, adminToken, passwordHash, ct))
      return Results.NotFound();

    authorizer.ResetAttempts(token, filename);
    return Results.NoContent();
  }
}
