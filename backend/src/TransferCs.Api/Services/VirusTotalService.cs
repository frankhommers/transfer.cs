using System.Text.Json;

namespace TransferCs.Api.Services;

public class VirusTotalService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public VirusTotalService(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async Task<string> ScanAsync(string filename, Stream content, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_apiKey), "apikey");
        form.Add(new StreamContent(content), "file", filename);

        var response = await _httpClient.PostAsync("https://www.virustotal.com/vtapi/v2/file/scan", form, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var permalink = doc.RootElement.TryGetProperty("permalink", out var p) ? p.GetString() ?? "" : "";

        return permalink;
    }
}
