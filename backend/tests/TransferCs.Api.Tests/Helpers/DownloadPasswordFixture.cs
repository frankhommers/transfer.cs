using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransferCs.Api.Models;
using TransferCs.Api.Services;

namespace TransferCs.Api.Tests.Helpers;

public sealed class DownloadPasswordFixture : IAsyncLifetime
{
  public const string Page =
    "<!doctype html><html><head><title>transfer.cs</title></head><body><div id=\"root\"></div></body></html>";

  public const string Password = "correct horse";

  private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"transfer-password-web-{Guid.NewGuid():N}");
  private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"transfer-password-data-{Guid.NewGuid():N}");

  public WebApplicationFactory<Program> Factory { get; private set; } = null!;

  public async Task InitializeAsync()
  {
    Directory.CreateDirectory(_webRoot);
    await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), Page);
    Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
      .UseWebRoot(_webRoot)
      .ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["TransferCs:InitialSiteId"] = "alpha",
          ["TransferCs:BasePath"] = _basePath,
          ["TransferCs:DownloadPasswordMaxAttempts"] = "3",
          ["TransferCs:HttpAuthUser"] = "user",
          ["TransferCs:HttpAuthPass"] = "password",
          ["TransferCs:Sites:alpha:Hosts:0"] = "alpha.test",
          ["TransferCs:Sites:alpha:Title"] = "Alpha files",
          ["TransferCs:Sites:beta:Hosts:0"] = "beta.test",
          ["TransferCs:Sites:beta:Title"] = "Beta files",
          ["TransferCs:Sites:beta:DownloadPasswordMaxAttempts"] = "1",
          ["TransferCs:Sites:beta:DownloadPasswordUnlockHours"] = "2"
        }))
      .ConfigureTestServices(services => services.AddSingleton(new DownloadPasswordHasher(1_000))));
  }

  public async Task DisposeAsync()
  {
    await Factory.DisposeAsync();
    Directory.Delete(_webRoot, true);
    if (Directory.Exists(_basePath))
      Directory.Delete(_basePath, true);
  }

  public HttpClient CreateClient(string host = "alpha.test")
  {
    HttpClient client = Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
      BaseAddress = new Uri($"http://{host}"),
      AllowAutoRedirect = false,
      HandleCookies = false
    });
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
      Convert.ToBase64String("user:password"u8.ToArray()));
    return client;
  }

  public static string UniqueToken(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

  public static async Task<(UploadedFile File, string AdminToken)> UploadAsync(HttpClient client, string token,
    string? password = Password, string filename = "file.txt", string content = "protected content",
    int? maxDownloads = null)
  {
    using StringContent body = new(content);
    body.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
    using HttpRequestMessage request = new(HttpMethod.Put, $"/put/{filename}") { Content = body };
    request.Headers.Accept.ParseAdd("application/json");
    request.Headers.Add("Token", token);
    request.Headers.Add("File-Lifetime", "1d");
    if (maxDownloads != null)
      request.Headers.Add("Max-Downloads", maxDownloads.Value.ToString());
    if (password != null)
      request.Headers.Add(DownloadAuthorizer.HeaderName, password);
    using HttpResponseMessage response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
    UploadedFile file = Assert.Single((await response.Content.ReadFromJsonAsync<UploadResponse>())!.Files);
    return (file, new Uri(file.AdminUrl).Fragment.TrimStart('#'));
  }

  public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
    string? password = null, string? cookie = null, string? accept = null)
  {
    using HttpRequestMessage request = new(method, path);
    if (password != null)
      request.Headers.Add(DownloadAuthorizer.HeaderName, password);
    if (cookie != null)
      request.Headers.Add("Cookie", cookie);
    if (accept != null)
      request.Headers.Accept.ParseAdd(accept);
    return await client.SendAsync(request);
  }

  public static async Task<HttpResponseMessage> UnlockAsync(HttpClient client, string token, string filename,
    string password)
  {
    using HttpRequestMessage request = new(HttpMethod.Post, $"/api/unlock/{token}/{filename}")
    {
      Content = JsonContent.Create(new Dictionary<string, string> { ["password"] = password })
    };
    return await client.SendAsync(request);
  }

  public static async Task<HttpResponseMessage> SetAdminPasswordAsync(HttpClient client, string token,
    string adminToken, string password, string filename = "file.txt") =>
    await SendAdminPasswordAsync(client, HttpMethod.Put, token, filename, adminToken,
      JsonContent.Create(new Dictionary<string, string> { ["password"] = password }));

  public static async Task<HttpResponseMessage> SetAdminPasswordJsonAsync(HttpClient client, string token,
    string? adminToken, string json, string mediaType = "application/json", string filename = "file.txt") =>
    await SendAdminPasswordAsync(client, HttpMethod.Put, token, filename, adminToken,
      new StringContent(json, Encoding.UTF8, mediaType));

  public static async Task<HttpResponseMessage> RemoveAdminPasswordAsync(HttpClient client, string token,
    string? adminToken, string filename = "file.txt") =>
    await SendAdminPasswordAsync(client, HttpMethod.Delete, token, filename, adminToken, null);

  public static async Task<JsonDocument> GetAdminMetadataAsync(HttpClient client, string token, string adminToken)
  {
    using HttpRequestMessage request = new(HttpMethod.Get, $"/api/admin/{token}/file.txt");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
    using HttpResponseMessage response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
  }

  private static async Task<HttpResponseMessage> SendAdminPasswordAsync(HttpClient client, HttpMethod method,
    string token, string filename, string? adminToken, HttpContent? content)
  {
    using HttpRequestMessage request = new(method, $"/api/admin/{token}/{filename}/password") { Content = content };
    if (adminToken != null)
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
    return await client.SendAsync(request);
  }

  public static string CookieHeader(HttpResponseMessage unlock)
  {
    string setCookie = Assert.Single(unlock.Headers.GetValues("Set-Cookie"));
    return setCookie[..setCookie.IndexOf(';')];
  }
}
