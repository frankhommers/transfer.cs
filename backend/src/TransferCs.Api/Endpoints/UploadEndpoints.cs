using Microsoft.Extensions.Options;
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
        app.MapPut("/put/{filename}", HandlePut);
        app.MapPut("/upload/{filename}", HandlePut);
        app.MapPut("/{filename}", HandlePut);
        app.MapPost("/", HandlePost);
        return app;
    }

    private static async Task<IResult> HandlePut(
        string filename,
        HttpRequest request,
        IStorageProvider storage,
        MetadataService metadataService,
        IOptions<TransferCsOptions> optionsAccessor,
        CancellationToken ct)
    {
        var options = optionsAccessor.Value;
        var sanitized = SanitizeHelper.SanitizeFilename(filename);
        var contentType = MimeHelper.GetMimeType(sanitized);

        // Buffer the request body to a temp file for scanning/encryption
        var tempPath = Path.Combine(options.TempPath, $"upload-{Guid.NewGuid():N}");
        long contentLength;

        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
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
                var clamService = new ClamAvService(options.ClamAvHost);
                var (isClean, status) = await clamService.ScanFileAsync(tempPath, ct);
                if (!isClean)
                    return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
            }

            var token = TokenService.Generate(options.RandomTokenLength);
            var deletionToken = TokenService.Generate(options.RandomTokenLength);

            var metadata = new FileMetadata
            {
                ContentType = contentType,
                ContentLength = contentLength,
                DeletionToken = deletionToken,
            };

            // Parse Max-Downloads header
            if (request.Headers.TryGetValue("Max-Downloads", out var maxDownloadsHeader)
                && int.TryParse(maxDownloadsHeader.FirstOrDefault(), out var maxDownloads)
                && maxDownloads > 0)
            {
                metadata.MaxDownloads = maxDownloads;
            }

            // Parse Max-Days header
            if (request.Headers.TryGetValue("Max-Days", out var maxDaysHeader)
                && int.TryParse(maxDaysHeader.FirstOrDefault(), out var maxDays)
                && maxDays > 0)
            {
                metadata.MaxDate = DateTime.UtcNow.AddDays(maxDays);
            }

            // Encryption
            Stream bodyStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var encryptPassword = request.Headers["X-Encrypt-Password"].FirstOrDefault() ?? "";
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

            var url = UrlHelper.ResolveUrl(request, $"/{token}/{sanitized}", options);
            var deleteUrl = UrlHelper.ResolveUrl(request, $"/{token}/{sanitized}/{deletionToken}", options);

            return new UploadResult(url, deleteUrl);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static async Task<IResult> HandlePost(
        HttpRequest request,
        IStorageProvider storage,
        MetadataService metadataService,
        IOptions<TransferCsOptions> optionsAccessor,
        CancellationToken ct)
    {
        var options = optionsAccessor.Value;

        if (!request.HasFormContentType)
            return Results.BadRequest("Expected multipart form data");

        var form = await request.ReadFormAsync(ct);
        var urls = new List<string>();

        foreach (var file in form.Files)
        {
            var sanitized = SanitizeHelper.SanitizeFilename(
                string.IsNullOrWhiteSpace(file.FileName) ? "_" : file.FileName);
            var contentType = MimeHelper.GetMimeType(sanitized);

            if (file.Length == 0)
                continue;

            if (options.MaxUploadSizeBytes > 0 && file.Length > options.MaxUploadSizeBytes)
                return Results.BadRequest($"File too large. Max size: {options.MaxUploadSizeKb} KB");

            var token = TokenService.Generate(options.RandomTokenLength);
            var deletionToken = TokenService.Generate(options.RandomTokenLength);

            var metadata = new FileMetadata
            {
                ContentType = contentType,
                ContentLength = file.Length,
                DeletionToken = deletionToken,
            };

            // Parse Max-Downloads header
            if (request.Headers.TryGetValue("Max-Downloads", out var maxDownloadsHeader)
                && int.TryParse(maxDownloadsHeader.FirstOrDefault(), out var maxDownloads)
                && maxDownloads > 0)
            {
                metadata.MaxDownloads = maxDownloads;
            }

            // Parse Max-Days header
            if (request.Headers.TryGetValue("Max-Days", out var maxDaysHeader)
                && int.TryParse(maxDaysHeader.FirstOrDefault(), out var maxDays)
                && maxDays > 0)
            {
                metadata.MaxDate = DateTime.UtcNow.AddDays(maxDays);
            }

            await metadataService.SaveAsync(token, sanitized, metadata, ct);
            await using var stream = file.OpenReadStream();
            await storage.PutAsync(token, sanitized, stream, contentType, (ulong)file.Length, ct);

            var url = UrlHelper.ResolveUrl(request, $"/{token}/{sanitized}", options);
            urls.Add(url);
        }

        if (urls.Count == 0)
            return Results.BadRequest("No files uploaded");

        return Results.Text(string.Join("\n", urls) + "\n", "text/plain");
    }

    /// <summary>
    /// Custom IResult that returns text/plain body with X-Url-Delete header.
    /// </summary>
    private sealed class UploadResult : IResult
    {
        private readonly string _url;
        private readonly string _deleteUrl;

        public UploadResult(string url, string deleteUrl)
        {
            _url = url;
            _deleteUrl = deleteUrl;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.ContentType = "text/plain";
            httpContext.Response.Headers["X-Url-Delete"] = _deleteUrl;
            await httpContext.Response.WriteAsync(_url + "\n");
        }
    }
}
