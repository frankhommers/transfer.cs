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

    string tempPath = Path.Combine(options.TempPath, $"upload-{Guid.NewGuid():N}");
    long contentLength;

    try
    {
      await using (FileStream fs = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
      {
        await request.Body.CopyToAsync(fs, ct);
        contentLength = fs.Length;
      }

      if (contentLength == 0)
        return Results.BadRequest("Empty upload");

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
      string? customToken = request.Headers["X-Token"].FirstOrDefault();
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
        DeletionToken = deletionToken
      };

      ApplyLifetime(metadata, request, options);

      // Encryption
      Stream bodyStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
      string encryptPassword = request.Headers["X-Encrypt-Password"].FirstOrDefault() ?? "";
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

      return new UploadResult(url, deleteUrl, expiry);
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
      string? customToken = request.Headers["X-Token"].FirstOrDefault();
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

      await metadataService.SaveAsync(token, sanitized, metadata, ct);
      await using Stream stream = file.OpenReadStream();
      await storage.PutAsync(token, sanitized, stream, contentType, (ulong)file.Length, ct);

      string url = UrlHelper.ResolveUrl(request, $"/{token}/{sanitized}", options);
      urls.Add(url);
    }

    if (urls.Count == 0)
      return Results.BadRequest("No files uploaded");

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

    public UploadResult(string url, string deleteUrl, DateTime? expires)
    {
      _url = url;
      _deleteUrl = deleteUrl;
      _expires = expires;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
      httpContext.Response.StatusCode = 200;
      httpContext.Response.ContentType = "text/plain";
      httpContext.Response.Headers["X-Url-Delete"] = _deleteUrl;
      if (_expires != null)
        httpContext.Response.Headers.Expires = ExpiresHelper.FormatHttpDate(_expires.Value);
      await httpContext.Response.WriteAsync(_url + "\n");
    }
  }
}