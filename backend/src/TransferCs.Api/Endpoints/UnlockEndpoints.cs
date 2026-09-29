using System.Text.Json;
using TransferCs.Api.Helpers;
using TransferCs.Api.Models;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class UnlockEndpoints
{
  public static WebApplication MapUnlockEndpoints(this WebApplication app)
  {
    app.MapPost("/api/unlock/{token}/{filename}", HandleUnlockAsync);
    return app;
  }

  private static async Task<IResult> HandleUnlockAsync(
    string token,
    string filename,
    HttpRequest request,
    HttpResponse response,
    MetadataService metadataService,
    DownloadAuthorizer authorizer,
    CancellationToken ct)
  {
    response.Headers.CacheControl = "no-store";
    FileMetadata? metadata = await metadataService.CheckAndLoadAsync(token, filename, false, ct);
    if (metadata == null)
      return Results.NotFound();
    if (!metadata.PasswordProtected)
      return Results.NoContent();
    if (!request.HasJsonContentType())
      return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

    UnlockRequest? body;
    try
    {
      body = await JsonSerializer.DeserializeAsync(request.Body, AppJsonContext.Default.UnlockRequest, ct);
    }
    catch (JsonException)
    {
      return Results.BadRequest("Invalid JSON body.");
    }

    if (string.IsNullOrEmpty(body?.Password))
      return Results.BadRequest("A password is required.");

    DownloadAuthorizationResult result = authorizer.VerifyPassword(body.Password, token, filename, metadata);
    if (!result.IsAllowed)
      return DownloadAccessResults.Denied(response, result);

    authorizer.AppendUnlockCookie(response, request.IsHttps, token, filename, metadata);
    return Results.NoContent();
  }
}
