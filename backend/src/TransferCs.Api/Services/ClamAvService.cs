using nClam;

namespace TransferCs.Api.Services;

public class ClamAvService
{
    private readonly ClamClient _client;

    public ClamAvService(string hostPort)
    {
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 3310;
        _client = new ClamClient(host, port);
    }

    public async Task<(bool IsClean, string Status)> ScanFileAsync(string filePath, CancellationToken ct)
    {
        var result = await _client.SendAndScanFileAsync(filePath, ct);

        return result.Result switch
        {
            ClamScanResults.Clean => (true, "clean"),
            ClamScanResults.VirusDetected => (false, $"virus detected: {result.InfectedFiles?.FirstOrDefault()?.VirusName ?? "unknown"}"),
            ClamScanResults.Error => (false, $"scan error: {result.RawResult}"),
            _ => (false, $"unknown result: {result.RawResult}")
        };
    }
}
