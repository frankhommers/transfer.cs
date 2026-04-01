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
    using MultipartFormDataContent form = new();
    form.Add(new StringContent(_apiKey), "apikey");
    form.Add(new StreamContent(content), "file", filename);

    HttpResponseMessage response =
      await _httpClient.PostAsync("https://www.virustotal.com/vtapi/v2/file/scan", form, ct);
    response.EnsureSuccessStatusCode();

    string json = await response.Content.ReadAsStringAsync(ct);
    using JsonDocument doc = JsonDocument.Parse(json);
    string permalink = doc.RootElement.TryGetProperty("permalink", out JsonElement p) ? p.GetString() ?? "" : "";

    return permalink;
  }
}