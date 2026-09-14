using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransferCs.Api.Models;
using TransferCs.Api.Tests.Helpers;

namespace TransferCs.Api.Tests.Endpoints;

public class HttpHeaderContractTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public HttpHeaderContractTests(WebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task Put_ReturnsCreatedLocationLinksAndPrivateJsonMetadataAsync()
  {
    byte[] content = "standard upload"u8.ToArray();
    using HttpRequestMessage request = PutRequest(content);
    request.Headers.Add("Content-Digest", Digest(content));
    request.Headers.Add("File-Lifetime", "1h");
    request.Headers.Add("Max-Downloads", "1");
    using HttpResponseMessage upload = await _client.SendAsync(request);
    UploadResponse result = (await upload.Content.ReadFromJsonAsync<UploadResponse>())!;
    UploadedFile file = Assert.Single(result.Files);

    Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    Assert.Equal(file.Url, upload.Headers.Location!.AbsoluteUri);
    Assert.Equal(file.AdminUrl, UploadResponseHeaders.AdminUrl(upload));
    Assert.Equal(file.DeleteUrl, UploadResponseHeaders.DeleteUrl(upload));
    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), file.Sha256);
    Assert.InRange(file.Expires!.Value, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    Assert.Equal("no-store", upload.Headers.CacheControl!.ToString());
    Assert.Contains("Accept", upload.Headers.Vary);
    AssertNoLegacyHeaders(upload);
    Assert.False(upload.Headers.Contains("Content-Digest"));
    Assert.False(upload.Headers.Contains("Repr-Digest"));

    using HttpRequestMessage headRequest = new(HttpMethod.Head, file.Url);
    using HttpResponseMessage head = await _client.SendAsync(headRequest);
    Assert.Equal(Digest(content), head.Headers.GetValues("Repr-Digest").Single());
    Assert.True(head.Headers.Contains("Sunset"));
    Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    Assert.Equal("1", head.Headers.GetValues("X-Remaining-Downloads").Single());
    AssertNoLegacyHeaders(head);

    using HttpRequestMessage range = new(HttpMethod.Get, file.Url);
    range.Headers.Range = new RangeHeaderValue(0, 2);
    using HttpResponseMessage partial = await _client.SendAsync(range);
    Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
    Assert.Equal(content[..3], await partial.Content.ReadAsByteArrayAsync());
    Assert.Equal(Digest(content), partial.Headers.GetValues("Repr-Digest").Single());
    Assert.False(partial.Headers.Contains("Content-Digest"));
    Assert.True(partial.Headers.Contains("Sunset"));
    Assert.False(partial.Headers.Contains("Link"));
    Assert.Equal("no-store", partial.Headers.CacheControl!.ToString());
    using HttpResponseMessage exhausted = await _client.GetAsync(file.Url);
    Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
  }

  [Theory]
  [InlineData("Content-Digest", "sha-256=:dGVzdA==:")]
  [InlineData("Content-Digest", " ")]
  [InlineData("File-Lifetime", "invalid")]
  [InlineData("File-Lifetime", "9999999999999999999999999d")]
  [InlineData("File-Lifetime", "2147483647d")]
  [InlineData("File-Lifetime", "0s")]
  [InlineData("Expected-Checksum", "ignored-is-unsafe")]
  [InlineData("X-Expected-Checksum", "ignored-is-unsafe")]
  [InlineData("Expires", "7d")]
  [InlineData("Max-Days", "7")]
  [InlineData("X-Encrypt-Password", "ignored-is-unsafe")]
  public async Task InvalidOrRemovedUploadHeader_IsRejectedBeforeStorageAsync(string header, string value)
  {
    string token = $"invalid-{Guid.NewGuid():N}";
    using HttpRequestMessage request = PutRequest("payload"u8.ToArray());
    request.Headers.Add("Token", token);
    Assert.True(request.Headers.TryAddWithoutValidation(header, value) ||
                request.Content!.Headers.TryAddWithoutValidation(header, value));
    using HttpResponseMessage response = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    using HttpResponseMessage download = await _client.GetAsync($"/{token}/file.txt");
    Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
  }

  [Fact]
  public async Task MismatchingDigest_LeavesTokenAvailableAsync()
  {
    string token = $"digest-{Guid.NewGuid():N}";
    using HttpRequestMessage request = PutRequest("payload"u8.ToArray());
    request.Headers.Add("Token", token);
    request.Headers.Add("Content-Digest", Digest("different"u8.ToArray()));
    using HttpResponseMessage rejected = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    using HttpRequestMessage retry = PutRequest("payload"u8.ToArray());
    retry.Headers.Add("Token", token);
    using HttpResponseMessage accepted = await _client.SendAsync(retry);
    Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
  }

  [Fact]
  public async Task MultipartJson_ReturnsIndependentLinksAndMetadataForDuplicateFilenamesAsync()
  {
    using MultipartFormDataContent multipart = new();
    multipart.Add(new StringContent("first"), "file", "same.txt");
    multipart.Add(new StringContent("second"), "file", "same.txt");
    using HttpRequestMessage request = new(HttpMethod.Post, "/") {Content = multipart};
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Headers.Add("File-Lifetime", "1h");
    using HttpResponseMessage response = await _client.SendAsync(request);
    UploadResponse result = (await response.Content.ReadFromJsonAsync<UploadResponse>())!;
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal(2, result.Files.Count);
    Assert.NotEqual(result.Files[0].Url, result.Files[1].Url);
    Assert.Null(response.Headers.Location);
    Assert.False(response.Headers.Contains("Link"));
    AssertNoLegacyHeaders(response);

    for (int index = 0; index < result.Files.Count; index++)
    {
      UploadedFile file = result.Files[index];
      string expected = index == 0 ? "first" : "second";
      using HttpResponseMessage download = await _client.GetAsync(file.Url);
      Assert.Equal(expected, await download.Content.ReadAsStringAsync());
      Assert.NotNull(file.Expires);
      Assert.NotEmpty(file.Sha256);
      using HttpRequestMessage admin = AdminRequest(file);
      using HttpResponseMessage metadata = await _client.SendAsync(admin);
      Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
      using HttpResponseMessage deletion = await _client.DeleteAsync(file.DeleteUrl);
      Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
      using HttpResponseMessage missing = await _client.GetAsync(file.Url);
      Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
  }

  [Theory]
  [InlineData("application/json", "application/json")]
  [InlineData("application/json;q=0", "text/plain")]
  [InlineData("text/plain;q=1, application/json;q=0.5", "text/plain")]
  [InlineData("*/*", "text/plain")]
  public async Task Upload_RespectsResponseFormatPreferenceAsync(string accept, string expected)
  {
    using HttpRequestMessage request = PutRequest("content"u8.ToArray());
    request.Headers.Accept.Clear();
    request.Headers.TryAddWithoutValidation("Accept", accept);
    using HttpResponseMessage response = await _client.SendAsync(request);
    Assert.Equal(expected, response.Content.Headers.ContentType!.MediaType);
  }

  [Fact]
  public async Task MultipartPlainText_ReturnsOneUrlPerFileAsync()
  {
    using MultipartFormDataContent multipart = new();
    multipart.Add(new StringContent("first"), "file", "first.txt");
    multipart.Add(new StringContent("second"), "file", "second.txt");
    using HttpResponseMessage response = await _client.PostAsync("/", multipart);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
    string[] urls = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    Assert.Equal(2, urls.Length);
    Assert.EndsWith("/first.txt", urls[0]);
    Assert.EndsWith("/second.txt", urls[1]);
  }

  [Fact]
  public async Task MultipartWithEmptyFile_RejectsWholeRequestAsync()
  {
    using MultipartFormDataContent multipart = new();
    multipart.Add(new StringContent("first"), "file", "first.txt");
    multipart.Add(new ByteArrayContent([]), "file", "empty.txt");
    using HttpResponseMessage response = await _client.PostAsync("/", multipart);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Contains("No files were uploaded", await response.Content.ReadAsStringAsync());
  }

  [Theory]
  [InlineData("Content-Digest")]
  [InlineData("Encrypt-Password")]
  public async Task Multipart_DoesNotSilentlyIgnorePutOnlyOptionsAsync(string header)
  {
    using MultipartFormDataContent multipart = new();
    multipart.Add(new StringContent("first"), "file", "first.txt");
    using HttpRequestMessage request = new(HttpMethod.Post, "/") {Content = multipart};
    request.Headers.Add(header, "value");
    using HttpResponseMessage response = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task LocationAndLinks_EscapeFilenameDelimitersAsync()
  {
    const string filename = "photo #1,>.txt";
    using HttpRequestMessage request = PutRequest("file"u8.ToArray());
    request.RequestUri = new Uri("/put/" + Uri.EscapeDataString(filename), UriKind.Relative);
    using HttpResponseMessage response = await _client.SendAsync(request);
    UploadedFile file = Assert.Single((await response.Content.ReadFromJsonAsync<UploadResponse>())!.Files);
    Assert.Equal(filename, file.Filename);
    Assert.Contains("%23", file.Url);
    Assert.Contains("%3E", file.Url);
    Assert.Equal(file.AdminUrl, UploadResponseHeaders.AdminUrl(response));
    using HttpResponseMessage download = await _client.GetAsync(file.Url);
    Assert.Equal("file", await download.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Cors_ExposesNewResponseHeadersAsync()
  {
    await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
      .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["TransferCs:CorsDomains"] = "https://client.example"
        })));
    using HttpClient client = factory.CreateClient();
    using HttpRequestMessage request = PutRequest("cors"u8.ToArray());
    request.Headers.Add("Origin", "https://client.example");
    using HttpResponseMessage response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.NotNull(factory.Services.GetService<Microsoft.AspNetCore.Cors.Infrastructure.ICorsService>());
    Assert.True(response.Headers.Contains("Access-Control-Expose-Headers"), response.ToString());
    string exposed = response.Headers.GetValues("Access-Control-Expose-Headers").Single();
    foreach (string header in new[] {"Location", "Link", "Repr-Digest", "Sunset"})
      Assert.Contains(header, exposed);
  }

  [Fact]
  public async Task JsonUpload_WorksWithReflectionDisabledAsync()
  {
    await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
      .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Clear())));
    using HttpClient client = factory.CreateClient();
    using HttpRequestMessage request = PutRequest("content"u8.ToArray());
    using HttpResponseMessage response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Single((await response.Content.ReadFromJsonAsync<UploadResponse>())!.Files);
  }

  [Fact]
  public async Task EncryptedUpload_PlaintextDigestRequiresDecryptionAsync()
  {
    byte[] content = "private plaintext"u8.ToArray();
    using HttpRequestMessage request = PutRequest(content);
    request.Headers.Add("Encrypt-Password", "test-password");
    request.Headers.Add("Content-Digest", Digest(content));
    using HttpResponseMessage upload = await _client.SendAsync(request);
    UploadedFile file = Assert.Single((await upload.Content.ReadFromJsonAsync<UploadResponse>())!.Files);
    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), file.Sha256);
    using HttpRequestMessage headRequest = new(HttpMethod.Head, file.Url);
    using HttpResponseMessage head = await _client.SendAsync(headRequest);
    using HttpResponseMessage ciphertext = await _client.GetAsync(file.Url);
    Assert.False(head.Headers.Contains("Repr-Digest"));
    Assert.False(ciphertext.Headers.Contains("Repr-Digest"));
    using HttpRequestMessage decrypt = new(HttpMethod.Get, file.Url);
    decrypt.Headers.Add("Decrypt-Password", "test-password");
    using HttpResponseMessage plaintext = await _client.SendAsync(decrypt);
    Assert.Equal(content, await plaintext.Content.ReadAsByteArrayAsync());
    Assert.Equal(Digest(content), plaintext.Headers.GetValues("Repr-Digest").Single());
  }

  [Fact]
  public async Task BearerAdmin_WorksWithGlobalBasicAuthAndRejectsLegacyTokenAsync()
  {
    await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
      .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["TransferCs:HttpAuthUser"] = "user", ["TransferCs:HttpAuthPass"] = "password"
        })));
    using HttpClient client = factory.CreateClient();
    using HttpRequestMessage request = PutRequest("admin"u8.ToArray());
    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNzd29yZA==");
    using HttpResponseMessage upload = await client.SendAsync(request);
    UploadedFile file = Assert.Single((await upload.Content.ReadFromJsonAsync<UploadResponse>())!.Files);
    using HttpRequestMessage admin = AdminRequest(file);
    using HttpResponseMessage metadata = await client.SendAsync(admin);
    Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
    using HttpRequestMessage legacy = new(HttpMethod.Get, admin.RequestUri);
    legacy.Headers.Add("Admin-Token", new Uri(file.AdminUrl).Fragment[1..]);
    using HttpResponseMessage rejected = await client.SendAsync(legacy);
    Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
    using HttpRequestMessage delete = AdminRequest(file);
    delete.Method = HttpMethod.Delete;
    using HttpResponseMessage deleted = await client.SendAsync(delete);
    Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
  }

  private static HttpRequestMessage PutRequest(byte[] content)
  {
    HttpRequestMessage request = new(HttpMethod.Put, "/put/file.txt") {Content = new ByteArrayContent(content)};
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    return request;
  }

  private static HttpRequestMessage AdminRequest(UploadedFile file)
  {
    Uri uri = new(file.AdminUrl);
    HttpRequestMessage request = new(HttpMethod.Get, "/api" + uri.AbsolutePath);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", uri.Fragment[1..]);
    return request;
  }

  private static string Digest(byte[] content) => $"sha-256=:{Convert.ToBase64String(SHA256.HashData(content))}:";

  private static void AssertNoLegacyHeaders(HttpResponseMessage response)
  {
    foreach (string header in new[] {"Checksum", "X-Checksum", "X-Url-Delete", "X-Url-Admin", "X-Remaining-Days"})
      Assert.False(response.Headers.Contains(header));
    Assert.Null(response.Content.Headers.Expires);
  }
}
