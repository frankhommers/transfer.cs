using nClam;

namespace TransferCs.Api.Services;

public class ClamAvService
{
  private readonly ClamClient _client;

  public ClamAvService(string hostPort)
  {
    string[] parts = hostPort.Split(':');
    string host = parts[0];
    int port = parts.Length > 1 && int.TryParse(parts[1], out int p) ? p : 3310;
    _client = new ClamClient(host, port);
  }

  public async Task<(bool IsClean, string Status)> ScanFileAsync(string filePath, CancellationToken ct)
  {
    ClamScanResult result = await _client.SendAndScanFileAsync(filePath, ct);

    return result.Result switch
    {
      ClamScanResults.Clean => (true, "clean"),
      ClamScanResults.VirusDetected => (false,
        $"virus detected: {result.InfectedFiles?.FirstOrDefault()?.VirusName ?? "unknown"}"),
      ClamScanResults.Error => (false, $"scan error: {result.RawResult}"),
      _ => (false, $"unknown result: {result.RawResult}")
    };
  }
}