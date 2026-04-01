using Microsoft.Extensions.Options;
using QRCoder;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
using TransferCs.Api.Models;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class PreviewEndpoints
{
  public static WebApplication MapPreviewEndpoints(this WebApplication app)
  {
    app.MapGet("/api/preview/{token}/{filename}", HandlePreviewAsync);
    return app;
  }

  private static async Task<IResult> HandlePreviewAsync(
    string token,
    string filename,
    HttpRequest request,
    MetadataService metadataService,
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    TransferCsOptions options = optionsAccessor.Value;
    FileMetadata? metadata = await metadataService.LoadAsync(token, filename, ct);
    if (metadata == null)
      return Results.NotFound();

    string url = UrlHelper.ResolveUrl(request, $"/{token}/{filename}", options);
    string downloadUrl = UrlHelper.ResolveUrl(request, $"/download/{token}/{filename}", options);

    // Generate QR code
    string qrCodeBase64;
    using (QRCodeGenerator qrGenerator = new())
    {
      QRCodeData qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
      PngByteQRCode qrCode = new(qrData);
      byte[] qrBytes = qrCode.GetGraphic(5);
      qrCodeBase64 = Convert.ToBase64String(qrBytes);
    }

    string previewType = GetPreviewType(metadata.ContentType);

    return Results.Json(new Models.PreviewResult
    {
      ContentType = metadata.ContentType,
      Filename = filename,
      Url = url,
      DownloadUrl = downloadUrl,
      Token = token,
      Hostname = request.Host.ToString(),
      ContentLength = metadata.ContentLength,
      QrCode = qrCodeBase64,
      PreviewType = previewType
    }, Models.AppJsonContext.Default.PreviewResult);
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