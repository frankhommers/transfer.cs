using Microsoft.Extensions.Options;
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
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    TransferCsOptions options = optionsAccessor.Value;
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
      string token;
      if (!string.IsNullOrEmpty(customToken))
      {
        string? validationError = TokenService.ValidateCustomToken(customToken);
        if (validationError != null)
          return Results.BadRequest(validationError);
        if (await storage.ExistsAsync(customToken, ct))
          return Results.Conflict("Token already in use");
        token = customToken;
      }
      else
      {
        token = TokenService.Generate(options.RandomTokenLength);
      }

      string deletionToken = TokenService.Generate(options.RandomTokenLength);

      FileMetadata metadata = new()
      {
        ContentType = contentType,
        ContentLength = contentLength,
        DeletionToken = deletionToken,
        // Hash of the plaintext as received, so it matches what the uploader computes
        // locally with sha256sum. PGP output is not deterministic, so hashing the stored
        // ciphertext would give the user nothing to compare against.
        Sha256 = sha256
      };

      ApplyLifetime(metadata, request, options);

      // Encryption
      Stream bodyStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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

      await metadataService.SaveAsync(token, sanitized, metadata, ct);
      await storage.PutAsync(token, sanitized, bodyStream, metadata.ContentType, (ulong)contentLength, ct);
      await bodyStream.DisposeAsync();

      string url = UrlHelper.ResolveUrl(request, $"/{token}/{sanitized}", options);
      string deleteUrl = UrlHelper.ResolveUrl(request, $"/{token}/{sanitized}/{deletionToken}", options);
      DateTime? expiry = ResolveExpiry(request, options);

      return new UploadResult(url, deleteUrl, expiry, sha256);
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
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    TransferCsOptions options = optionsAccessor.Value;

    if (!request.HasFormContentType)
      return Results.BadRequest("Expected multipart form data");

    IFormCollection form = await request.ReadFormAsync(ct);
    List<string> urls = [];
    List<string> checksums = [];

    foreach (IFormFile file in form.Files)
    {
      string sanitized = SanitizeHelper.SanitizeFilename(
        string.IsNullOrWhiteSpace(file.FileName) ? "_" : file.FileName);
      string contentType = MimeHelper.GetMimeType(sanitized);

      if (file.Length == 0)
        continue;

      if (options.MaxUploadSizeBytes > 0 && file.Length > options.MaxUploadSizeBytes)
        return Results.BadRequest($"File too large. Max size: {options.MaxUploadSizeKb} KB");

      // Custom or random token
      string? customToken = (request.Headers["Token"].FirstOrDefault() ?? request.Headers["X-Token"].FirstOrDefault());
      string token;
      if (!string.IsNullOrEmpty(customToken))
      {
        string? validationError = TokenService.ValidateCustomToken(customToken);
        if (validationError != null)
          return Results.BadRequest(validationError);
        if (await storage.ExistsAsync(customToken, ct))
          return Results.Conflict("Token already in use");
        token = customToken;
      }
      else
      {
        token = TokenService.Generate(options.RandomTokenLength);
      }

      string deletionToken = TokenService.Generate(options.RandomTokenLength);

      FileMetadata metadata = new()
      {
        ContentType = contentType,
        ContentLength = file.Length,
        DeletionToken = deletionToken
      };

      ApplyLifetime(metadata, request, options);

      // Hash while streaming into storage; no temp file and no second pass.
      await using (ChecksumHelper.HashingReadStream stream = new(file.OpenReadStream()))
      {
        await storage.PutAsync(token, sanitized, stream, contentType, (ulong)file.Length, ct);
        metadata.Sha256 = stream.Sha256Hex;
      }

      // Metadata is written after the blob so it carries the digest.
      await metadataService.SaveAsync(token, sanitized, metadata, ct);
      checksums.Add(metadata.Sha256);

      string url = UrlHelper.ResolveUrl(request, $"/{token}/{sanitized}", options);
      urls.Add(url);
    }

    if (urls.Count == 0)
      return Results.BadRequest("No files uploaded");

    // One header value per file would be ambiguous, so only emit it for a single-file post.
    if (checksums.Count == 1 && !string.IsNullOrEmpty(checksums[0]))
    {
      string value = ChecksumHelper.Format(checksums[0]);
      request.HttpContext.Response.Headers["Checksum"] = value;
      request.HttpContext.Response.Headers["X-Checksum"] = value;
    }

    return Results.Text(string.Join("\n", urls) + "\n", "text/plain");
  }

  /// <summary>
  /// Custom IResult that returns text/plain body with X-Url-Delete and Expires headers.
  /// </summary>
  private sealed class UploadResult : IResult
  {
    private readonly string _url;
    private readonly string _deleteUrl;
    private readonly DateTime? _expires;
    private readonly string _sha256;

    public UploadResult(string url, string deleteUrl, DateTime? expires, string sha256)
    {
      _url = url;
      _deleteUrl = deleteUrl;
      _expires = expires;
      _sha256 = sha256;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
      httpContext.Response.StatusCode = 200;
      httpContext.Response.ContentType = "text/plain";
      httpContext.Response.Headers["X-Url-Delete"] = _deleteUrl;
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