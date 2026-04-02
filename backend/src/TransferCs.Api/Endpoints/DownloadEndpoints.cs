using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
using TransferCs.Api.Models;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Endpoints;

public static class DownloadEndpoints
{
  public static WebApplication MapDownloadEndpoints(this WebApplication app)
  {
    // Action routes must be registered BEFORE the two-segment routes
    // to avoid /{token}/{filename} matching /{action}/{token}/{filename}
    app.MapMethods("/{action}/{token}/{filename}",
      ["HEAD"],
      async (string action, string token, string filename,
        HttpRequest request,
        IStorageProvider storage,
        MetadataService metadataService,
        IOptions<TransferCsOptions> optionsAccessor,
        CancellationToken ct) =>
      {
        if (!IsValidAction(action))
          return Results.NotFound();
        return await HandleHeadAsync(token, filename, request, storage, metadataService, optionsAccessor, ct);
      });

    app.MapMethods("/{action}/{token}/{filename}",
      ["GET"],
      async (string action, string token, string filename,
        HttpRequest request,
        HttpResponse response,
        IStorageProvider storage,
        MetadataService metadataService,
        IOptions<TransferCsOptions> optionsAccessor,
        CancellationToken ct) =>
      {
        if (!IsValidAction(action))
          return Results.NotFound();
        return await HandleGetAsync(action, token, filename, request, response, storage, metadataService, optionsAccessor,
          ct);
      });

    app.MapMethods("/{token}/{filename}",
      ["HEAD"],
      (string token, string filename,
          HttpRequest request,
          IStorageProvider storage,
          MetadataService metadataService,
          IOptions<TransferCsOptions> optionsAccessor,
          CancellationToken ct) =>
        HandleHeadAsync(token, filename, request, storage, metadataService, optionsAccessor, ct));

    app.MapGet("/{token}/{filename}",
      (string token, string filename,
          HttpRequest request,
          HttpResponse response,
          IStorageProvider storage,
          MetadataService metadataService,
          IOptions<TransferCsOptions> optionsAccessor,
          CancellationToken ct) =>
        HandleGetAsync("get", token, filename, request, response, storage, metadataService, optionsAccessor, ct));

    return app;
  }

  private static bool IsValidAction(string action)
  {
    return action is "download" or "get" or "inline";
  }

  private static async Task<IResult> HandleHeadAsync(
    string token,
    string filename,
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    FileMetadata? metadata = await metadataService.CheckAndLoadAsync(token, filename, false, ct);
    if (metadata == null)
      return Results.NotFound();

    try
    {
      ulong contentLength = await storage.HeadAsync(token, filename, ct);

      return Results.Ok(new
      {
        // Headers will be set below
      });
    }
    catch (Exception ex) when (storage.IsNotExist(ex))
    {
      return Results.NotFound();
    }
  }

  private static async Task<IResult> HandleGetAsync(
    string action,
    string token,
    string filename,
    HttpRequest request,
    HttpResponse response,
    IStorageProvider storage,
    MetadataService metadataService,
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    FileMetadata? metadata = await metadataService.CheckAndLoadAsync(token, filename, true, ct);
    if (metadata == null)
      return Results.NotFound();

    try
    {
      string? rangeHeader = request.Headers.Range.FirstOrDefault();
      StorageRange? range = StorageRange.Parse(rangeHeader);

      (Stream stream, ulong contentLength) = await storage.GetAsync(token, filename, range, ct);

      string contentType = metadata.ContentType;

      // Decrypt if requested and file is encrypted
      string decryptPassword = (request.Headers["Decrypt-Password"].FirstOrDefault() ?? request.Headers["X-Decrypt-Password"].FirstOrDefault()) ?? "";
      if (!string.IsNullOrEmpty(decryptPassword) && metadata.Encrypted)
      {
        stream = await EncryptionService.DecryptAsync(stream, decryptPassword);
        contentLength = (ulong)stream.Length;
        if (!string.IsNullOrEmpty(metadata.DecryptedContentType))
          contentType = metadata.DecryptedContentType;
      }

      string disposition = action == "inline"
        ? $"inline; filename=\"{filename}\""
        : $"attachment; filename=\"{filename}\"";

      response.Headers.ContentDisposition = disposition;
      response.Headers["X-Remaining-Downloads"] = metadata.RemainingDownloads;
      response.Headers["X-Remaining-Days"] = metadata.RemainingDays;

      if (range != null && range.ContentRange != null)
      {
        response.StatusCode = 206;
        response.Headers.ContentRange = range.ContentRange;
        response.ContentType = contentType;
        response.ContentLength = (long)contentLength;
        await stream.CopyToAsync(response.Body, ct);
        await stream.DisposeAsync();
        return Results.Empty;
      }

      response.ContentType = contentType;
      response.ContentLength = (long)contentLength;
      await stream.CopyToAsync(response.Body, ct);
      await stream.DisposeAsync();
      return Results.Empty;
    }
    catch (Exception ex) when (storage.IsNotExist(ex))
    {
      return Results.NotFound();
    }
  }
}