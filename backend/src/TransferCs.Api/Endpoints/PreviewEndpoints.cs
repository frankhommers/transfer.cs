using Microsoft.Extensions.Options;
using QRCoder;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class PreviewEndpoints
{
    public static WebApplication MapPreviewEndpoints(this WebApplication app)
    {
        app.MapGet("/api/preview/{token}/{filename}", HandlePreview);
        return app;
    }

    private static async Task<IResult> HandlePreview(
        string token,
        string filename,
        HttpRequest request,
        MetadataService metadataService,
        IOptions<TransferCsOptions> optionsAccessor,
        CancellationToken ct)
    {
        var options = optionsAccessor.Value;
        var metadata = await metadataService.LoadAsync(token, filename, ct);
        if (metadata == null)
            return Results.NotFound();

        var url = UrlHelper.ResolveUrl(request, $"/{token}/{filename}", options);
        var downloadUrl = UrlHelper.ResolveUrl(request, $"/download/{token}/{filename}", options);

        // Generate QR code
        string qrCodeBase64;
        using (var qrGenerator = new QRCodeGenerator())
        {
            var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var qrCode = new PngByteQRCode(qrData);
            var qrBytes = qrCode.GetGraphic(5);
            qrCodeBase64 = Convert.ToBase64String(qrBytes);
        }

        var previewType = GetPreviewType(metadata.ContentType);

        return Results.Json(new
        {
            contentType = metadata.ContentType,
            filename,
            url,
            downloadUrl,
            token,
            hostname = request.Host.ToString(),
            contentLength = metadata.ContentLength,
            qrCode = qrCodeBase64,
            previewType
        });
    }

    private static string GetPreviewType(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "image";
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return "video";
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return "audio";
        if (contentType.Contains("markdown", StringComparison.OrdinalIgnoreCase))
            return "markdown";
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return "text";
        return "generic";
    }
}
