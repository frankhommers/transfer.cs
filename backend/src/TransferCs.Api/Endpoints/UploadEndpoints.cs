using Microsoft.Extensions.Primitives;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
using TransferCs.Api.Models;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Endpoints;

public static class UploadEndpoints
{
  public static WebApplication MapUploadEndpoints(this WebApplication app)
  {
    app.MapPut("/put/{filename}", HandlePutAsync);
    app.MapPut("/upload/{filename}", HandlePutAsync);
    app.MapPut("/{filename}", HandlePutAsync);
    app.MapPost("/", HandlePostAsync);
    app.MapPost("/archive", HandleArchiveAsync);
    return app;
  }

  private static readonly string[] _removedHeaders =
    ["Expires", "Max-Days", "Expected-Checksum", "X-Expected-Checksum", "X-Token", "X-Encrypt-Password"];

  private static IResult? ValidateUploadHeaders(HttpRequest request, TransferCsOptions options, out DateTime? expiry)
  {
    expiry = null;
    foreach (string header in _removedHeaders)
    {
      if (request.Headers.ContainsKey(header))
      {
        return Results.BadRequest($"{header} is no longer supported. Use File-Lifetime, Content-Digest, " +
                                  "Token and Encrypt-Password; see the API documentation.");
      }
    }

    if (request.Headers.TryGetValue("File-Lifetime", out StringValues lifetime))
    {
      expiry = ExpiresHelper.Parse(lifetime.ToString());
      if (expiry == null)
        return Results.BadRequest("Invalid File-Lifetime. Use a positive duration or a future date.");
    }
    else if (options.PurgeDays > 0)
    {
      expiry = DateTime.UtcNow.AddDays(options.PurgeDays);
    }

    return null;
  }

  private static void ApplyLifetime(FileMetadata metadata, HttpRequest request, DateTime? expiry)
  {
    // Max-Downloads
    if (request.Headers.TryGetValue("Max-Downloads", out StringValues maxDownloadsHeader)
        && int.TryParse(maxDownloadsHeader.FirstOrDefault(), out int maxDownloads)
        && maxDownloads > 0)
      metadata.MaxDownloads = maxDownloads;

    // Expiry
    if (expiry != null) metadata.MaxDate = expiry.Value;
  }

  private static async Task<IResult> HandlePutAsync(
    string filename,
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    SiteContext siteContext,
    DiskSpaceGuard diskSpace,
    EncryptionService encryption,
    CancellationToken ct)
  {
    TransferCsOptions options = siteContext.Site.Options;
    string sanitized = SanitizeHelper.SanitizeFilename(filename);
    IResult? headerError = ValidateUploadHeaders(request, options, out DateTime? expiry);
    if (headerError != null)
      return headerError;

    string? expectedChecksumHeader = request.Headers.ContainsKey("Content-Digest")
      ? request.Headers["Content-Digest"].ToString() : null;
    string? expectedChecksum = null;
    if (expectedChecksumHeader != null)
    {
      if (!HttpDigestHelper.TryParse(expectedChecksumHeader, out string parsed))
        return Results.BadRequest("Content-Digest must contain one SHA-256 digest: sha-256=:<base64>:");
      expectedChecksum = parsed;
    }

    string tempDir = options.ResolvedTempPath;
    string tempPath = Path.Combine(tempDir, $"upload-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    long contentLength;
    string sha256;
    try
    {
      await using (Stream fs = diskSpace.CreateFile(tempPath))
      {
        // Hash rides along on the copy that already happens - no second pass over the bytes.
        (contentLength, sha256) = await ChecksumHelper.CopyAndHashAsync(request.Body, fs, ct);
      }

      if (contentLength == 0)
        return Results.BadRequest("Empty upload");

      // A truncated upload (dropped connection, proxy timeout) would otherwise be stored
      // silently as a valid file.
      if (request.ContentLength is { } declaredLength && declaredLength != contentLength)
        return Results.BadRequest(
          $"Incomplete upload: expected {declaredLength} bytes, received {contentLength}.");

      if (expectedChecksum != null && !ChecksumHelper.Matches(expectedChecksum, sha256))
        return Results.BadRequest(
          $"Checksum mismatch: expected sha256:{expectedChecksum}, got sha256:{sha256}.");

      return await StoreUploadAsync(sanitized, tempPath, contentLength, sha256, expiry,
        request, storage, metadataService, options, encryption, ct);
    }
    finally
    {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  private static async Task<IResult> StoreUploadAsync(
    string sanitized,
    string tempPath,
    long contentLength,
    string sha256,
    DateTime? expiry,
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    TransferCsOptions options,
    EncryptionService encryption,
    CancellationToken ct)
  {
    string contentType = MimeHelper.GetMimeType(sanitized);
    string? reservedToken = null;
    bool uploadCompleted = false;
    try
    {
      if (options.MaxUploadSizeBytes > 0 && contentLength > options.MaxUploadSizeBytes)
        return Results.BadRequest($"File too large. Max size: {options.MaxUploadSizeKb} KB");

      // ClamAV prescan
      if (options.PerformClamAvPrescan && !string.IsNullOrEmpty(options.ClamAvHost))
      {
        ClamAvService clamService = new(options.ClamAvHost);
        (bool isClean, string status) = await clamService.ScanFileAsync(tempPath, ct);
        if (!isClean)
          return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
      }

      // Custom or random token
      string? customToken = request.Headers["Token"].FirstOrDefault();
      (string? token, IResult? reservationError) = await ReserveTokenAsync(
        customToken, options.RandomTokenLength, storage, ct);
      if (reservationError != null)
        return reservationError;
      reservedToken = token!;

      string deletionToken = TokenService.GenerateAdminToken();
      string adminToken = TokenService.GenerateAdminToken();

      FileMetadata metadata = new()
      {
        Generation = Guid.NewGuid().ToString("N"),
        ContentType = contentType,
        ContentLength = contentLength,
        DeletionToken = deletionToken,
        AdminToken = adminToken,
        // Hash the file or generated ZIP before encryption; PGP output is not deterministic.
        Sha256 = sha256
      };

      ApplyLifetime(metadata, request, expiry);

      // Encryption
      await using Stream sourceStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
      Stream bodyStream = sourceStream;
      string encryptPassword = request.Headers["Encrypt-Password"].FirstOrDefault() ?? "";
      if (!string.IsNullOrEmpty(encryptPassword))
      {
        bodyStream = await encryption.EncryptAsync(bodyStream, encryptPassword);
        metadata.Encrypted = true;
        metadata.DecryptedContentType = contentType;
        metadata.ContentType = "text/plain; charset=utf-8";
        contentLength = bodyStream.Length;
        metadata.ContentLength = contentLength;
      }
      await using Stream storedStream = bodyStream;

      await storage.PutAsync(reservedToken, sanitized, storedStream, metadata.ContentType, (ulong)contentLength, ct);
      await metadataService.SaveAsync(reservedToken, sanitized, metadata, ct);
      uploadCompleted = true;

      string escapedFilename = Uri.EscapeDataString(sanitized);
      string url = UrlHelper.ResolveUrl(request, $"/{reservedToken}/{escapedFilename}", options);
      string deleteUrl = UrlHelper.ResolveUrl(request, $"/{reservedToken}/{escapedFilename}/{deletionToken}", options);
      string adminUrl = UrlHelper.ResolveUrl(request, $"/admin/{reservedToken}/{escapedFilename}", options) + $"#{adminToken}";
      return new UploadResult([new UploadedFile(sanitized, url, deleteUrl, adminUrl, sha256, expiry)]);
    }
    finally
    {
      if (reservedToken != null)
      {
        if (!uploadCompleted)
        {
          await storage.DeleteAsync(reservedToken, sanitized, CancellationToken.None);
          await storage.DeleteAsync(reservedToken, $"{sanitized}.metadata", CancellationToken.None);
        }
        await storage.ReleaseTokenAsync(reservedToken, CancellationToken.None);
      }
    }
  }

  private static async Task<IResult> HandleArchiveAsync(
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    SiteContext siteContext,
    DiskSpaceGuard diskSpace,
    EncryptionService encryption,
    CancellationToken ct)
  {
    TransferCsOptions options = siteContext.Site.Options;
    IResult? headerError = ValidateUploadHeaders(request, options, out DateTime? expiry);
    if (headerError != null)
      return headerError;
    if (request.Headers.ContainsKey("Content-Digest") || request.Headers.ContainsKey("Encrypt-Password"))
      return Results.BadRequest("Content-Digest and Encrypt-Password are supported only for PUT uploads.");
    if (!request.HasFormContentType)
      return Results.BadRequest("Expected multipart form data");

    await using BufferedUploadForm uploadForm = await BufferedUploadForm.ReadAsync(request, diskSpace, ct);
    List<IFormFile> files = uploadForm.Form.Files.ToList();
    if (files.Count == 0)
      return Results.BadRequest("No files uploaded");
    if (options.MaxUploadSizeBytes > 0 && files.Sum(file => file.Length) > options.MaxUploadSizeBytes)
      return Results.BadRequest($"Files too large. Max combined size: {options.MaxUploadSizeKb} KB");

    Directory.CreateDirectory(options.ResolvedTempPath);
    string tempPath = Path.Combine(options.ResolvedTempPath, $"archive-{Guid.NewGuid():N}.zip");
    try
    {
      long contentLength;
      string sha256;
      await using (Stream archiveStream = diskSpace.CreateFile(tempPath))
      {
        await ZipUploadHelper.WriteAsync(archiveStream, files, ct);
        contentLength = archiveStream.Length;
        if (options.MaxUploadSizeBytes > 0 && contentLength > options.MaxUploadSizeBytes)
          return Results.BadRequest($"ZIP too large. Max size: {options.MaxUploadSizeKb} KB");
        archiveStream.Position = 0;
        sha256 = await ChecksumHelper.ComputeSha256Async(archiveStream, ct);
      }

      return await StoreUploadAsync("files.zip", tempPath, contentLength, sha256, expiry,
        request, storage, metadataService, options, encryption, ct);
    }
    finally
    {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  private static async Task<IResult> HandlePostAsync(
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    SiteContext siteContext,
    DiskSpaceGuard diskSpace,
    CancellationToken ct)
  {
    TransferCsOptions options = siteContext.Site.Options;
    IResult? headerError = ValidateUploadHeaders(request, options, out DateTime? expiry);
    if (headerError != null)
      return headerError;
    if (request.Headers.ContainsKey("Content-Digest") || request.Headers.ContainsKey("Encrypt-Password"))
      return Results.BadRequest("Content-Digest and Encrypt-Password are supported only for PUT uploads.");

    if (!request.HasFormContentType)
      return Results.BadRequest("Expected multipart form data");

    await using BufferedUploadForm uploadForm = await BufferedUploadForm.ReadAsync(request, diskSpace, ct);
    List<IFormFile> files = uploadForm.Form.Files.ToList();
    if (files.Count == 0)
      return Results.BadRequest("No files uploaded");
    if (files.Any(file => file.Length == 0))
      return Results.BadRequest("Empty files are not supported. No files were uploaded.");
    if (options.MaxUploadSizeBytes > 0 && files.Any(file => file.Length > options.MaxUploadSizeBytes))
      return Results.BadRequest($"File too large. Max size: {options.MaxUploadSizeKb} KB");

    string? requestCustomToken = request.Headers["Token"].FirstOrDefault();
    if (!string.IsNullOrEmpty(requestCustomToken) && files.Count > 1)
      return Results.BadRequest("A custom token can only be used with one file per request.");

    List<UploadedFile> uploadedFiles = [];
    List<(string Token, string Filename)> completedUploads = [];

    try
    {
      foreach (IFormFile file in files)
      {
        string sanitized = SanitizeHelper.SanitizeFilename(
          string.IsNullOrWhiteSpace(file.FileName) ? "_" : file.FileName);
        string? reservedToken = null;
        bool uploadCompleted = false;
        try
        {
          string contentType = MimeHelper.GetMimeType(sanitized);

          (string? token, IResult? reservationError) = await ReserveTokenAsync(
            requestCustomToken, options.RandomTokenLength, storage, ct);
          if (reservationError != null)
          {
            await DeleteUploadsAsync(storage, completedUploads);
            return reservationError;
          }
          reservedToken = token!;

          string deletionToken = TokenService.GenerateAdminToken();
          string adminToken = TokenService.GenerateAdminToken();

          FileMetadata metadata = new()
          {
            Generation = Guid.NewGuid().ToString("N"),
            ContentType = contentType,
            ContentLength = file.Length,
            DeletionToken = deletionToken,
            AdminToken = adminToken
          };

          ApplyLifetime(metadata, request, expiry);

          await using (ChecksumHelper.HashingReadStream stream = new(file.OpenReadStream()))
          {
            await storage.PutAsync(reservedToken, sanitized, stream, contentType, (ulong)file.Length, ct);
            metadata.Sha256 = stream.Sha256Hex;
          }

          await metadataService.SaveAsync(reservedToken, sanitized, metadata, ct);
          uploadCompleted = true;
          completedUploads.Add((reservedToken, sanitized));

          string escapedFilename = Uri.EscapeDataString(sanitized);
          string url = UrlHelper.ResolveUrl(request, $"/{reservedToken}/{escapedFilename}", options);
          string deleteUrl = UrlHelper.ResolveUrl(request,
            $"/{reservedToken}/{escapedFilename}/{deletionToken}", options);
          string adminUrl = UrlHelper.ResolveUrl(request, $"/admin/{reservedToken}/{escapedFilename}", options) +
                            $"#{adminToken}";
          uploadedFiles.Add(new UploadedFile(sanitized, url, deleteUrl, adminUrl, metadata.Sha256,
            expiry));
        }
        finally
        {
          if (reservedToken != null)
          {
            if (!uploadCompleted)
            {
              await storage.DeleteAsync(reservedToken, sanitized, CancellationToken.None);
              await storage.DeleteAsync(reservedToken, $"{sanitized}.metadata", CancellationToken.None);
            }
            await storage.ReleaseTokenAsync(reservedToken, CancellationToken.None);
          }
        }
      }
    }
    catch
    {
      await DeleteUploadsAsync(storage, completedUploads);
      throw;
    }

    return new UploadResult(uploadedFiles);
  }

  private static async Task DeleteUploadsAsync(IStorageProvider storage,
    IEnumerable<(string Token, string Filename)> uploads)
  {
    foreach ((string token, string filename) in uploads)
    {
      await storage.DeleteAsync(token, filename, CancellationToken.None);
      await storage.DeleteAsync(token, $"{filename}.metadata", CancellationToken.None);
    }
  }

  private static async Task<(string? Token, IResult? Error)> ReserveTokenAsync(string? customToken,
    int randomTokenLength, IStorageProvider storage, CancellationToken ct)
  {
    if (!string.IsNullOrEmpty(customToken))
    {
      string? validationError = TokenService.ValidateCustomToken(customToken);
      if (validationError != null)
        return (null, Results.BadRequest(validationError));
      if (!await storage.TryReserveTokenAsync(customToken, ct))
        return (null, Results.Conflict("Token already in use"));
      return (customToken, null);
    }

    for (int attempt = 0; attempt < 100; attempt++)
    {
      string token = TokenService.Generate(randomTokenLength);
      if (await storage.TryReserveTokenAsync(token, ct))
        return (token, null);
    }

    return (null, Results.Problem("Could not allocate an upload token.", statusCode: 503));
  }
}
