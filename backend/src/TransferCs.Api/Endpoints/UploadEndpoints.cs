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
    return app;
  }

  /// <summary>
  /// Resolves the expiry date from the Expires request header, falling back to Max-Days header,
  /// then PurgeDays config.
  /// </summary>
  private static DateTime? ResolveExpiry(HttpRequest request, TransferCsOptions options)
  {
    // Expires header: "7d", "12h30m", "2026-04-15T00:00:00Z", etc.
    string? expiresHeader = request.Headers["Expires"].FirstOrDefault();
    DateTime? expiry = ExpiresHelper.Parse(expiresHeader);
    if (expiry != null)
      return expiry;

    // Legacy Max-Days header
    if (request.Headers.TryGetValue("Max-Days", out StringValues maxDaysHeader)
        && int.TryParse(maxDaysHeader.FirstOrDefault(), out int maxDays)
        && maxDays > 0)
      return DateTime.UtcNow.AddDays(maxDays);

    // Config fallback
    if (options.PurgeDays > 0)
      return DateTime.UtcNow.AddDays(options.PurgeDays);

    return null;
  }

  private static void ApplyLifetime(FileMetadata metadata, HttpRequest request, TransferCsOptions options)
  {
    // Max-Downloads
    if (request.Headers.TryGetValue("Max-Downloads", out StringValues maxDownloadsHeader)
        && int.TryParse(maxDownloadsHeader.FirstOrDefault(), out int maxDownloads)
        && maxDownloads > 0)
      metadata.MaxDownloads = maxDownloads;

    // Expiry
    DateTime? expiry = ResolveExpiry(request, options);
    if (expiry != null) metadata.MaxDate = expiry.Value;
  }

  private static async Task<IResult> HandlePutAsync(
    string filename,
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    SiteContext siteContext,
    CancellationToken ct)
  {
    TransferCsOptions options = siteContext.Site.Options;
    string sanitized = SanitizeHelper.SanitizeFilename(filename);
    string contentType = MimeHelper.GetMimeType(sanitized);

    // Expected-Checksum is validated before anything is stored, so a mismatch leaves no trace.
    string? expectedChecksumHeader = request.Headers["Expected-Checksum"].FirstOrDefault()
                                     ?? request.Headers["X-Expected-Checksum"].FirstOrDefault();
    string? expectedChecksum = null;
    if (!string.IsNullOrWhiteSpace(expectedChecksumHeader))
    {
      if (!ChecksumHelper.TryParseExpected(expectedChecksumHeader, out string parsed, out string checksumError))
        return Results.BadRequest(checksumError);
      expectedChecksum = parsed;
    }

    string tempDir = options.ResolvedTempPath;
    string tempPath = Path.Combine(tempDir, $"upload-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    long contentLength;
    string sha256;
    string? reservedToken = null;
    bool uploadCompleted = false;

    try
    {
      await using (FileStream fs = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
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
      string? customToken = (request.Headers["Token"].FirstOrDefault() ?? request.Headers["X-Token"].FirstOrDefault());
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
        // Hash of the plaintext as received, so it matches what the uploader computes
        // locally with sha256sum. PGP output is not deterministic, so hashing the stored
        // ciphertext would give the user nothing to compare against.
        Sha256 = sha256
      };

      ApplyLifetime(metadata, request, options);

      // Encryption
      await using Stream sourceStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
      Stream bodyStream = sourceStream;
      string encryptPassword = (request.Headers["Encrypt-Password"].FirstOrDefault() ?? request.Headers["X-Encrypt-Password"].FirstOrDefault()) ?? "";
      if (!string.IsNullOrEmpty(encryptPassword))
      {
        bodyStream = await EncryptionService.EncryptAsync(bodyStream, encryptPassword);
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

      string url = UrlHelper.ResolveUrl(request, $"/{reservedToken}/{sanitized}", options);
      string deleteUrl = UrlHelper.ResolveUrl(request, $"/{reservedToken}/{sanitized}/{deletionToken}", options);
      string adminUrl = UrlHelper.ResolveUrl(request, $"/admin/{reservedToken}/{sanitized}", options) + $"#{adminToken}";
      DateTime? expiry = ResolveExpiry(request, options);

      return new UploadResult(url, deleteUrl, adminUrl, expiry, sha256);
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
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  private static async Task<IResult> HandlePostAsync(
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    SiteContext siteContext,
    CancellationToken ct)
  {
    TransferCsOptions options = siteContext.Site.Options;

    if (!request.HasFormContentType)
      return Results.BadRequest("Expected multipart form data");

    IFormCollection form = await request.ReadFormAsync(ct);
    List<IFormFile> files = form.Files.Where(file => file.Length > 0).ToList();
    if (files.Count == 0)
      return Results.BadRequest("No files uploaded");
    if (options.MaxUploadSizeBytes > 0 && files.Any(file => file.Length > options.MaxUploadSizeBytes))
      return Results.BadRequest($"File too large. Max size: {options.MaxUploadSizeKb} KB");

    string? requestCustomToken = request.Headers["Token"].FirstOrDefault() ??
                                 request.Headers["X-Token"].FirstOrDefault();
    if (!string.IsNullOrEmpty(requestCustomToken) && files.Count > 1)
      return Results.BadRequest("A custom token can only be used with one file per request.");

    List<string> urls = [];
    List<string> checksums = [];
    List<string> adminUrls = [];
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

        ApplyLifetime(metadata, request, options);

        await using (ChecksumHelper.HashingReadStream stream = new(file.OpenReadStream()))
        {
          await storage.PutAsync(reservedToken, sanitized, stream, contentType, (ulong)file.Length, ct);
          metadata.Sha256 = stream.Sha256Hex;
        }

        await metadataService.SaveAsync(reservedToken, sanitized, metadata, ct);
        uploadCompleted = true;
        completedUploads.Add((reservedToken, sanitized));
        checksums.Add(metadata.Sha256);

        string url = UrlHelper.ResolveUrl(request, $"/{reservedToken}/{sanitized}", options);
        urls.Add(url);
        adminUrls.Add(UrlHelper.ResolveUrl(request, $"/admin/{reservedToken}/{sanitized}", options) +
                      $"#{adminToken}");
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

    // One header value per file would be ambiguous, so only emit it for a single-file post.
    if (checksums.Count == 1 && !string.IsNullOrEmpty(checksums[0]))
    {
      string value = ChecksumHelper.Format(checksums[0]);
      request.HttpContext.Response.Headers["Checksum"] = value;
      request.HttpContext.Response.Headers["X-Checksum"] = value;
    }

    if (adminUrls.Count == 1)
      request.HttpContext.Response.Headers["X-Url-Admin"] = adminUrls[0];

    return Results.Text(string.Join("\n", urls) + "\n", "text/plain");
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

  /// <summary>
  /// Custom IResult that returns text/plain body with X-Url-Delete and Expires headers.
  /// </summary>
  private sealed class UploadResult : IResult
  {
    private readonly string _url;
    private readonly string _deleteUrl;
    private readonly string _adminUrl;
    private readonly DateTime? _expires;
    private readonly string _sha256;

    public UploadResult(string url, string deleteUrl, string adminUrl, DateTime? expires, string sha256)
    {
      _url = url;
      _deleteUrl = deleteUrl;
      _adminUrl = adminUrl;
      _expires = expires;
      _sha256 = sha256;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
      httpContext.Response.StatusCode = 200;
      httpContext.Response.ContentType = "text/plain";
      httpContext.Response.Headers["X-Url-Delete"] = _deleteUrl;
      httpContext.Response.Headers["X-Url-Admin"] = _adminUrl;
      if (_expires != null)
        httpContext.Response.Headers.Expires = ExpiresHelper.FormatHttpDate(_expires.Value);

      // Header, not body: the body is exactly the URL and CLI workflows pipe it straight on.
      if (!string.IsNullOrEmpty(_sha256))
      {
        string value = ChecksumHelper.Format(_sha256);
        httpContext.Response.Headers["Checksum"] = value;
        httpContext.Response.Headers["X-Checksum"] = value;
      }

      await httpContext.Response.WriteAsync(_url + "\n");
    }
  }
}
