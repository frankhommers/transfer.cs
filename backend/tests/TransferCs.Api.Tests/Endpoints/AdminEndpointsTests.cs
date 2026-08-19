using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Tests.Endpoints;

public class AdminEndpointsTests
{
  [Fact]
  public async Task Upload_ReturnsAdminUrlWithTokenInFragment()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:AdminTokenLength"] = "0"
    });
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-fragment");

    using HttpResponseMessage response = await UploadAsync(client, token, "file.txt");

    Assert.True(response.Headers.TryGetValues("X-Url-Admin", out IEnumerable<string>? values));
    Uri adminUrl = new(values.Single());
    Uri deleteUrl = new(response.Headers.GetValues("X-Url-Delete").Single());
    Assert.Equal($"/admin/{token}/file.txt", adminUrl.AbsolutePath);
    Assert.Equal(32, adminUrl.Fragment.TrimStart('#').Length);
    Assert.DoesNotContain(adminUrl.Fragment.TrimStart('#'), adminUrl.AbsolutePath);
    Assert.NotEqual(deleteUrl.Segments[^1].TrimEnd('/'), adminUrl.Fragment.TrimStart('#'));
  }

  [Fact]
  public async Task SingleFileMultipartUpload_ReturnsAdminUrl()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();
    using MultipartFormDataContent multipart = new();
    multipart.Add(new ByteArrayContent("multipart admin"u8.ToArray()), "file", "file.txt");

    using HttpResponseMessage response = await client.PostAsync("/", multipart);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(32, new Uri(response.Headers.GetValues("X-Url-Admin").Single())
      .Fragment.TrimStart('#').Length);
  }

  [Fact]
  public async Task AdminMetadata_RequiresCorrectAdminToken()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-auth");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");

    using HttpResponseMessage missing = await client.GetAsync($"/api/admin/{token}/file.txt");
    using HttpRequestMessage wrongRequest = new(HttpMethod.Get, $"/api/admin/{token}/file.txt");
    wrongRequest.Headers.Add("Admin-Token", "wrong");
    using HttpResponseMessage wrong = await client.SendAsync(wrongRequest);

    Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
  }

  [Fact]
  public async Task AdminMetadata_ListsForwardedDownloadIpWhenEnabled()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:DownloadLogEnabled"] = "true",
      ["TransferCs:TrustedProxies"] = "*"
    });
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-log");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");
    string adminToken = GetAdminToken(upload);

    using HttpRequestMessage download = new(HttpMethod.Get, $"/{token}/file.txt");
    download.Headers.Add("X-Forwarded-For", "198.51.100.42");
    using HttpResponseMessage downloadResponse = await client.SendAsync(download);
    downloadResponse.EnsureSuccessStatusCode();

    using HttpRequestMessage adminRequest = new(HttpMethod.Get, $"/api/admin/{token}/file.txt");
    adminRequest.Headers.Add("Admin-Token", adminToken);
    using HttpResponseMessage response = await client.SendAsync(adminRequest);
    using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(1, document.RootElement.GetProperty("downloads").GetInt32());
    Assert.Equal(1, document.RootElement.GetProperty("downloadLogTotal").GetInt32());
    JsonElement entry = document.RootElement.GetProperty("downloadLog")[0];
    Assert.Equal("198.51.100.42", entry.GetProperty("ipAddress").GetString());
  }

  [Fact]
  public async Task AdminMetadata_DoesNotLogIpByDefault()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-no-log");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");
    string adminToken = GetAdminToken(upload);
    using HttpResponseMessage download = await client.GetAsync($"/{token}/file.txt");
    download.EnsureSuccessStatusCode();

    using HttpRequestMessage request = new(HttpMethod.Get, $"/api/admin/{token}/file.txt");
    request.Headers.Add("Admin-Token", adminToken);
    using HttpResponseMessage response = await client.SendAsync(request);
    using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    Assert.Equal(0, document.RootElement.GetProperty("downloadLogTotal").GetInt32());
    Assert.Empty(document.RootElement.GetProperty("downloadLog").EnumerateArray());
  }

  [Fact]
  public async Task AdminMetadata_DisablesCachingAndIndexing()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-headers");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");

    using HttpRequestMessage request = new(HttpMethod.Get, $"/api/admin/{token}/file.txt");
    request.Headers.Add("Admin-Token", GetAdminToken(upload));
    using HttpResponseMessage response = await client.SendAsync(request);

    Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
  }

  [Fact]
  public async Task AdminDelete_UsesHeaderTokenAndDeletesFile()
  {
    await using WebApplicationFactory<Program> factory = CreateFactoryWithoutJsonReflection();
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-delete");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");

    using HttpRequestMessage request = new(HttpMethod.Delete, $"/api/admin/{token}/file.txt");
    request.Headers.Add("Admin-Token", GetAdminToken(upload));
    using HttpResponseMessage response = await client.SendAsync(request);
    using HttpResponseMessage download = await client.GetAsync($"/{token}/file.txt");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
  }

  [Fact]
  public async Task AdminDelete_MissingTokenReturnsNotFoundWhenBasicAuthIsEnabled()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:HttpAuthUser"] = "user",
      ["TransferCs:HttpAuthPass"] = "password"
    });
    using HttpClient client = factory.CreateClient();

    using HttpResponseMessage response = await client.DeleteAsync("/api/admin/missing/file.txt");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task DownloadLog_KeepsTotalWhenRecentEntriesAreTrimmed()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:DownloadLogEnabled"] = "true",
      ["TransferCs:DownloadLogMaxEntries"] = "1",
      ["TransferCs:TrustedProxies"] = "*"
    });
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-trim");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");

    await DownloadFromAsync(client, token, "198.51.100.1");
    await DownloadFromAsync(client, token, "198.51.100.2");
    using JsonDocument document = await GetAdminMetadataAsync(client, token, GetAdminToken(upload));

    Assert.Equal(2, document.RootElement.GetProperty("downloadLogTotal").GetInt32());
    Assert.Single(document.RootElement.GetProperty("downloadLog").EnumerateArray());
    Assert.Equal("198.51.100.2", document.RootElement.GetProperty("downloadLog")[0]
      .GetProperty("ipAddress").GetString());
  }

  [Fact]
  public async Task DownloadLog_ZeroRetentionUsesOneEntryMinimum()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:DownloadLogEnabled"] = "true",
      ["TransferCs:DownloadLogMaxEntries"] = "0",
      ["TransferCs:TrustedProxies"] = "*"
    });
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-zero-retention");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");

    await DownloadFromAsync(client, token, "198.51.100.3");
    using JsonDocument document = await GetAdminMetadataAsync(client, token, GetAdminToken(upload));

    Assert.Equal(1, document.RootElement.GetProperty("downloadLogTotal").GetInt32());
    Assert.Single(document.RootElement.GetProperty("downloadLog").EnumerateArray());
  }

  [Fact]
  public async Task Head_DoesNotConsumeDownloadOrAddLogEntry()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:DownloadLogEnabled"] = "true",
      ["TransferCs:TrustedProxies"] = "*"
    });
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-head");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt", 1);
    using HttpRequestMessage head = new(HttpMethod.Head, $"/{token}/file.txt");
    head.Headers.Add("X-Forwarded-For", "198.51.100.4");

    using HttpResponseMessage headResponse = await client.SendAsync(head);
    using JsonDocument document = await GetAdminMetadataAsync(client, token, GetAdminToken(upload));

    Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
    Assert.Equal(0, document.RootElement.GetProperty("downloads").GetInt32());
    Assert.Equal(0, document.RootElement.GetProperty("downloadLogTotal").GetInt32());
  }

  [Fact]
  public async Task MetadataSidecar_IsNotDownloadable()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-sidecar");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");

    using HttpResponseMessage response = await client.GetAsync($"/{token}/file.txt.metadata");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task MissingPayload_DoesNotConsumeDownload()
  {
    Dictionary<string, string?> settings = new()
    {
      ["TransferCs:DownloadLogEnabled"] = "true"
    };
    await using WebApplicationFactory<Program> factory = CreateFactory(settings);
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-missing-payload");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");
    SiteStorageFactory storageFactory = factory.Services.GetRequiredService<SiteStorageFactory>();
    SiteResolver siteResolver = factory.Services.GetRequiredService<SiteResolver>();
    IStorageProvider storage = storageFactory.Get(siteResolver.LegacySite);
    await storage.DeleteAsync(token, "file.txt");

    using HttpResponseMessage download = await client.GetAsync($"/{token}/file.txt");
    using JsonDocument metadata = await GetAdminMetadataAsync(client, token, GetAdminToken(upload));

    Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    Assert.Equal(0, metadata.RootElement.GetProperty("downloads").GetInt32());
    Assert.Equal(0, metadata.RootElement.GetProperty("downloadLogTotal").GetInt32());
  }

  [Fact]
  public async Task BundleDownload_IsIncludedInDownloadLog()
  {
    Dictionary<string, string?> settings = new()
    {
      ["TransferCs:DownloadLogEnabled"] = "true",
      ["TransferCs:TrustedProxies"] = "*"
    };
    await using WebApplicationFactory<Program> factory = CreateFactory(settings);
    using HttpClient client = factory.CreateClient();
    string token = UniqueToken("admin-bundle");
    using HttpResponseMessage upload = await UploadAsync(client, token, "file.txt");

    using HttpRequestMessage bundleRequest = new(HttpMethod.Get, $"/bundle.zip?files={token}/file.txt");
    bundleRequest.Headers.Add("X-Forwarded-For", "203.0.113.20");
    using HttpResponseMessage bundle = await client.SendAsync(bundleRequest);
    using JsonDocument metadata = await GetAdminMetadataAsync(client, token, GetAdminToken(upload));

    Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
    Assert.Equal(1, metadata.RootElement.GetProperty("downloads").GetInt32());
    Assert.Equal(1, metadata.RootElement.GetProperty("downloadLogTotal").GetInt32());
  }

  private static WebApplicationFactory<Program> CreateFactory(
    Dictionary<string, string?>? settings = null)
  {
    settings ??= [];
    return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(settings)));
  }

  private static WebApplicationFactory<Program> CreateFactoryWithoutJsonReflection() =>
    CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
      services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Clear())));

  private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, string filename,
    int? maxDownloads = null)
  {
    using ByteArrayContent content = new("admin test"u8.ToArray());
    content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
    using HttpRequestMessage request = new(HttpMethod.Put, $"/put/{filename}") { Content = content };
    request.Headers.Add("Token", token);
    if (maxDownloads != null)
      request.Headers.Add("Max-Downloads", maxDownloads.Value.ToString());
    HttpResponseMessage response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return response;
  }

  private static string GetAdminToken(HttpResponseMessage upload)
  {
    string value = upload.Headers.GetValues("X-Url-Admin").Single();
    return new Uri(value).Fragment.TrimStart('#');
  }

  private static string UniqueToken(string prefix) =>
    $"{prefix}-{Guid.NewGuid():N}";

  private static async Task DownloadFromAsync(HttpClient client, string token, string ipAddress)
  {
    using HttpRequestMessage request = new(HttpMethod.Get, $"/{token}/file.txt");
    request.Headers.Add("X-Forwarded-For", ipAddress);
    using HttpResponseMessage response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
  }

  private static async Task<JsonDocument> GetAdminMetadataAsync(HttpClient client, string token,
    string adminToken)
  {
    using HttpRequestMessage request = new(HttpMethod.Get, $"/api/admin/{token}/file.txt");
    request.Headers.Add("Admin-Token", adminToken);
    using HttpResponseMessage response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
  }
}
