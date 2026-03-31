using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;

namespace TransferCs.Api.Endpoints;

public static class ScanEndpoints
{
    public static WebApplication MapScanEndpoints(this WebApplication app)
    {
        app.MapPut("/{filename}/scan", HandleClamAvScan);
        app.MapPut("/{filename}/virustotal", HandleVirusTotalScan);
        return app;
    }

    private static async Task<IResult> HandleClamAvScan(
        string filename,
        HttpRequest request,
        IOptions<TransferCsOptions> optionsAccessor,
        CancellationToken ct)
    {
        var options = optionsAccessor.Value;
        if (string.IsNullOrEmpty(options.ClamAvHost))
            return Results.BadRequest("ClamAV not configured");

        var tempPath = Path.Combine(options.TempPath, $"scan-{Guid.NewGuid():N}");
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await request.Body.CopyToAsync(fs, ct);
            }

            var clamService = new ClamAvService(options.ClamAvHost);
            var (isClean, status) = await clamService.ScanFileAsync(tempPath, ct);

            return Results.Json(new { filename, isClean, status });
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static async Task<IResult> HandleVirusTotalScan(
        string filename,
        HttpRequest request,
        IHttpClientFactory httpClientFactory,
        IOptions<TransferCsOptions> optionsAccessor,
        CancellationToken ct)
    {
        var options = optionsAccessor.Value;
        if (string.IsNullOrEmpty(options.VirusTotalKey))
            return Results.BadRequest("VirusTotal not configured");

        var httpClient = httpClientFactory.CreateClient();
        var vtService = new VirusTotalService(httpClient, options.VirusTotalKey);

        var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        ms.Position = 0;

        var permalink = await vtService.ScanAsync(filename, ms, ct);

        return Results.Json(new { filename, permalink });
    }
}
