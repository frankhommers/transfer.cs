using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class ScanEndpoints
{
  public static WebApplication MapScanEndpoints(this WebApplication app)
  {
    app.MapPut("/{filename}/scan", HandleClamAvScanAsync);
    app.MapPut("/{filename}/virustotal", HandleVirusTotalScanAsync);
    return app;
  }

  private static async Task<IResult> HandleClamAvScanAsync(
    string filename,
    HttpRequest request,
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    TransferCsOptions options = optionsAccessor.Value;
    if (string.IsNullOrEmpty(options.ClamAvHost))
      return Results.BadRequest("ClamAV not configured");

    string tempPath = Path.Combine(options.TempPath, $"scan-{Guid.NewGuid():N}");
    try
    {
      await using (FileStream fs = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
      {
        await request.Body.CopyToAsync(fs, ct);
      }

      ClamAvService clamService = new(options.ClamAvHost);
      (bool isClean, string status) = await clamService.ScanFileAsync(tempPath, ct);

      return Results.Json(new Models.ScanResult { Filename = filename, IsClean = isClean, Status = status },
        Models.AppJsonContext.Default.ScanResult);
    }
    finally
    {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  private static async Task<IResult> HandleVirusTotalScanAsync(
    string filename,
    HttpRequest request,
    IHttpClientFactory httpClientFactory,
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    TransferCsOptions options = optionsAccessor.Value;
    if (string.IsNullOrEmpty(options.VirusTotalKey))
      return Results.BadRequest("VirusTotal not configured");

    HttpClient httpClient = httpClientFactory.CreateClient();
    VirusTotalService vtService = new(httpClient, options.VirusTotalKey);

    string tempPath = Path.Combine(options.TempPath, $"vt-{Guid.NewGuid():N}");
    try
    {
      await using (FileStream fs = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
      {
        await request.Body.CopyToAsync(fs, ct);
      }

      await using FileStream scanStream = new(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
      string permalink = await vtService.ScanAsync(filename, scanStream, ct);

      return Results.Json(new Models.VirusTotalResult { Filename = filename, Permalink = permalink },
        Models.AppJsonContext.Default.VirusTotalResult);
    }
    finally
    {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}