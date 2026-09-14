using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TransferCs.Api.Models;
using TransferCs.Api.Tests.Helpers;

namespace TransferCs.Api.Tests.Endpoints;

public class ArchiveUploadTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public ArchiveUploadTests(WebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task MultipleFiles_CreateOneZipWithSharedLifetimeAndDownloadLimitAsync()
  {
    string token = $"zip-{Guid.NewGuid():N}";
    using MultipartFormDataContent content = new();
    content.Add(new StringContent("first"), "file", "first.txt");
    content.Add(new StringContent("second"), "file", "second.txt");
    using HttpRequestMessage request = ArchiveRequest(content);
    request.Headers.Add("Token", token);
    request.Headers.Add("File-Lifetime", "1h");
    request.Headers.Add("Max-Downloads", "1");
    using HttpResponseMessage response = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    UploadedFile file = Assert.Single((await response.Content.ReadFromJsonAsync<UploadResponse>())!.Files);
    Assert.Equal("files.zip", file.Filename);
    Assert.EndsWith($"/{token}/files.zip", file.Url);
    Assert.Equal(file.Url, response.Headers.Location!.AbsoluteUri);
    Assert.Equal(file.AdminUrl, UploadResponseHeaders.AdminUrl(response));
    Assert.Equal(file.DeleteUrl, UploadResponseHeaders.DeleteUrl(response));
    Assert.NotNull(file.Expires);

    using HttpRequestMessage headRequest = new(HttpMethod.Head, file.Url);
    using HttpResponseMessage head = await _client.SendAsync(headRequest);
    Assert.Equal("application/zip", head.Content.Headers.ContentType!.MediaType);
    Assert.Equal("1", head.Headers.GetValues("X-Remaining-Downloads").Single());
    Assert.True(head.Headers.Contains("Sunset"));

    using HttpResponseMessage download = await _client.GetAsync(file.Url);
    byte[] zip = await download.Content.ReadAsByteArrayAsync();
    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(zip)), file.Sha256);
    using MemoryStream stream = new(zip);
    using ZipArchive archive = new(stream, ZipArchiveMode.Read);
    Assert.Equal(2, archive.Entries.Count);
    Assert.Equal("first", await ReadEntryAsync(archive.GetEntry("first.txt")!));
    Assert.Equal("second", await ReadEntryAsync(archive.GetEntry("second.txt")!));
    using HttpResponseMessage exhausted = await _client.GetAsync(file.Url);
    Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
    using HttpResponseMessage original = await _client.GetAsync($"/{token}/first.txt");
    Assert.Equal(HttpStatusCode.NotFound, original.StatusCode);

    Uri adminUrl = new(file.AdminUrl);
    using HttpRequestMessage adminRequest = new(HttpMethod.Get, "/api" + adminUrl.AbsolutePath);
    adminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminUrl.Fragment[1..]);
    using HttpResponseMessage admin = await _client.SendAsync(adminRequest);
    Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    using HttpResponseMessage deletion = await _client.DeleteAsync(file.DeleteUrl);
    Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
  }

  [Fact]
  public async Task ZipEntries_FlattenPathsPreserveEmptyFilesAndDisambiguateNamesAsync()
  {
    using MultipartFormDataContent content = new();
    content.Add(new StringContent("first"), "file", "same.txt");
    content.Add(new StringContent("second"), "file", "same.txt");
    content.Add(new StringContent("third"), "file", "SAME.TXT");
    content.Add(new StringContent("path"), "file", "../../nested.txt");
    content.Add(new StringContent("windows"), "file", "..\\..\\windows.txt");
    content.Add(new ByteArrayContent([]), "file", "empty.txt");
    using HttpRequestMessage request = ArchiveRequest(content);
    using HttpResponseMessage response = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    UploadedFile file = Assert.Single((await response.Content.ReadFromJsonAsync<UploadResponse>())!.Files);
    byte[] zip = await _client.GetByteArrayAsync(file.Url);
    using MemoryStream stream = new(zip);
    using ZipArchive archive = new(stream, ZipArchiveMode.Read);
    Assert.Equal(6, archive.Entries.Count);
    Assert.Equal(6, archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    Assert.All(archive.Entries, entry =>
    {
      Assert.DoesNotContain("/", entry.FullName);
      Assert.DoesNotContain("\\", entry.FullName);
    });
    Assert.Equal("first", await ReadEntryAsync(archive.GetEntry("same.txt")!));
    Assert.Equal("second", await ReadEntryAsync(archive.GetEntry("same (2).txt")!));
    Assert.Equal("third", await ReadEntryAsync(archive.GetEntry("SAME (3).TXT")!));
    Assert.Equal("path", await ReadEntryAsync(archive.GetEntry("nested.txt")!));
    Assert.Equal("windows", await ReadEntryAsync(archive.GetEntry("windows.txt")!));
    Assert.Equal(0, archive.GetEntry("empty.txt")!.Length);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CombinedInputAndZipOutputLimits_AreEnforcedAsync(bool exceedZipOverhead)
  {
    string tempPath = Path.Combine(Path.GetTempPath(), $"transfer-zip-limit-{Guid.NewGuid():N}");
    await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
      .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["TransferCs:MaxUploadSizeKb"] = "1",
          ["TransferCs:TempPath"] = tempPath
        })));
    using HttpClient client = factory.CreateClient();
    using MultipartFormDataContent content = new();
    if (exceedZipOverhead)
    {
      for (int index = 0; index < 20; index++)
        content.Add(new ByteArrayContent([]), "file", $"empty-{index}.txt");
    }
    else
    {
      content.Add(new ByteArrayContent(new byte[600]), "file", "first.bin");
      content.Add(new ByteArrayContent(new byte[600]), "file", "second.bin");
    }
    using HttpRequestMessage request = ArchiveRequest(content);
    string token = $"limit-{Guid.NewGuid():N}";
    request.Headers.Add("Token", token);
    using HttpResponseMessage response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Contains(exceedZipOverhead ? "ZIP too large" : "combined size", await response.Content.ReadAsStringAsync());
    using HttpResponseMessage missing = await client.GetAsync($"/{token}/files.zip");
    Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    if (Directory.Exists(tempPath))
    {
      Assert.Empty(Directory.GetFiles(tempPath, "archive-*.zip"));
      Directory.Delete(tempPath);
    }
  }

  [Fact]
  public async Task ArchivePlainText_ReturnsSingleUrlAsync()
  {
    using MultipartFormDataContent content = new();
    content.Add(new StringContent("first"), "file", "first.txt");
    content.Add(new StringContent("second"), "file", "second.txt");
    using HttpResponseMessage response = await _client.PostAsync("/archive", content);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal(response.Headers.Location!.AbsoluteUri + "\n", await response.Content.ReadAsStringAsync());
    Assert.NotEmpty(UploadResponseHeaders.AdminUrl(response));
  }

  [Theory]
  [InlineData("Content-Digest")]
  [InlineData("Encrypt-Password")]
  [InlineData("Expected-Checksum")]
  public async Task UnsupportedArchiveOptions_AreRejectedAsync(string header)
  {
    using MultipartFormDataContent content = new();
    content.Add(new StringContent("file"), "file", "file.txt");
    using HttpRequestMessage request = ArchiveRequest(content);
    request.Headers.Add(header, "value");
    using HttpResponseMessage response = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task ArchiveWithoutFiles_IsRejectedAsync()
  {
    using MultipartFormDataContent content = new();
    content.Add(new StringContent("value"), "field");
    using HttpResponseMessage response = await _client.PostAsync("/archive", content);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private static HttpRequestMessage ArchiveRequest(MultipartFormDataContent content)
  {
    HttpRequestMessage request = new(HttpMethod.Post, "/archive") {Content = content};
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    return request;
  }

  private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
  {
    using StreamReader reader = new(entry.Open());
    return await reader.ReadToEndAsync();
  }
}
