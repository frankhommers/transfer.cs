using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Storage;
using TransferCs.Api.Tests.Helpers;

namespace TransferCs.Api.Tests.Endpoints;

public sealed class DiskSpaceEndpointsTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), $"disk-endpoints-{Guid.NewGuid():N}");
  private readonly TestDiskSpaceProbe _probe = new();
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;
  private string TempPath => Path.Combine(_directory, "temp");
  private string DataPath => Path.Combine(_directory, "data");

  public DiskSpaceEndpointsTests()
  {
    _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services =>
      {
        services.Configure<TransferCsOptions>(options =>
        {
          options.BasePath = DataPath;
          options.TempPath = TempPath;
          options.MinFreeDiskSpaceMb = 1;
          options.ClamAvHost = "unused.invalid";
          options.VirusTotalKey = "test-only";
        });
        services.Replace(ServiceDescriptor.Singleton<IDiskSpaceProbe>(_probe));
      }));
    _client = _factory.CreateClient();
  }

  [Theory]
  [InlineData("PUT", "/file.txt")]
  [InlineData("POST", "/")]
  [InlineData("POST", "/archive")]
  [InlineData("PUT", "/file.txt/scan")]
  [InlineData("PUT", "/file.txt/virustotal")]
  public async Task LowTemporarySpace_Returns507WithoutLeavingFilesAsync(string method, string path)
  {
    _probe.AvailableBytes = _ => 0;
    using HttpRequestMessage request = CreateUpload(method, path);
    using HttpResponseMessage response = await _client.SendAsync(request);
    await AssertStorageFailureAsync(response);
    AssertNoFiles();
  }

  [Theory]
  [InlineData("PUT", "/file.txt")]
  [InlineData("POST", "/")]
  [InlineData("POST", "/archive")]
  public async Task LowPayloadSpace_RollsBackAndAllowsRetryAsync(string method, string path)
  {
    _probe.AvailableBytes = path => path.StartsWith(DataPath, StringComparison.Ordinal) ? 0 : long.MaxValue;
    using HttpRequestMessage request = CreateUpload(method, path);
    request.Headers.Add("Token", "disk-retry");
    using HttpResponseMessage response = await _client.SendAsync(request);
    await AssertStorageFailureAsync(response);
    AssertNoFiles();

    _probe.AvailableBytes = _ => long.MaxValue;
    using HttpRequestMessage retry = CreateUpload(method, path);
    retry.Headers.Add("Token", "disk-retry");
    using HttpResponseMessage accepted = await _client.SendAsync(retry);
    Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    Assert.Empty(Directory.GetFiles(TempPath));
  }

  [Theory]
  [InlineData("archive-")]
  [InlineData("encrypt-")]
  public async Task GeneratedFiles_AreGuardedAndCleanedUpAsync(string prefix)
  {
    _probe.AvailableBytes = path => Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)
      ? 0 : long.MaxValue;
    using HttpRequestMessage request = prefix == "archive-"
      ? CreateUpload("POST", "/archive") : CreateUpload("PUT", "/file.txt");
    if (prefix == "encrypt-")
      request.Headers.Add("Encrypt-Password", "test-password");
    using HttpResponseMessage response = await _client.SendAsync(request);
    await AssertStorageFailureAsync(response);
    AssertNoFiles();
  }

  [Fact]
  public async Task FailureSavingNewMetadata_RollsBackPayloadAsync()
  {
    _probe.AvailableBytes = path => path.Contains(".metadata.", StringComparison.Ordinal) ? 0 : long.MaxValue;
    using HttpRequestMessage request = CreateUpload("PUT", "/file.txt");
    using HttpResponseMessage response = await _client.SendAsync(request);
    await AssertStorageFailureAsync(response);
    AssertNoFiles();
  }

  [Fact]
  public async Task MultipartFailureAfterFirstFile_RollsBackWholeBatchAsync()
  {
    _probe.AvailableBytes = path => path.Contains(".second.txt.", StringComparison.Ordinal) ? 0 : long.MaxValue;
    using MultipartFormDataContent content = new();
    content.Add(new StringContent("first"), "file", "first.txt");
    content.Add(new StringContent("second"), "file", "second.txt");
    using HttpResponseMessage response = await _client.PostAsync("/", content);
    await AssertStorageFailureAsync(response);
    AssertNoFiles();
  }

  [Fact]
  public async Task ChunkedUpload_StopsWhenSpaceRunsOutAsync()
  {
    _probe.AvailableBytes = _ => 1024 * 1024 + 64 * 1024 + 100_000 -
      (Directory.Exists(TempPath) ? Directory.GetFiles(TempPath).Sum(path => new FileInfo(path).Length) : 0);
    using HttpRequestMessage request = CreateUpload("PUT", "/file.txt");
    request.Headers.TransferEncodingChunked = true;
    using HttpResponseMessage response = await _client.SendAsync(request);
    await AssertStorageFailureAsync(response);
    AssertNoFiles();
    Assert.Contains(_probe.CheckedPaths, path => Path.GetFileName(path).StartsWith("upload-"));
  }

  [Fact]
  public async Task LowSpace_PreservesDownloadsAndDeletionAsync()
  {
    using HttpRequestMessage request = CreateUpload("PUT", "/file.txt");
    using HttpResponseMessage upload = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    _probe.AvailableBytes = _ => 0;

    using HttpResponseMessage download = await _client.GetAsync(upload.Headers.Location);
    Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    Assert.Equal(200_000, (await download.Content.ReadAsByteArrayAsync()).Length);
    using HttpResponseMessage delete = await _client.DeleteAsync(UploadResponseHeaders.DeleteUrl(upload));
    Assert.True(delete.IsSuccessStatusCode);
    AssertNoFiles();
  }

  [Theory]
  [InlineData("zip")]
  [InlineData("tar")]
  [InlineData("tar.gz")]
  public async Task DownloadBundles_UseGuardedTemporaryFilesAsync(string extension)
  {
    using HttpRequestMessage request = CreateUpload("PUT", "/file.txt");
    using HttpResponseMessage upload = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    _probe.AvailableBytes = path => Path.GetFileName(path).StartsWith("bundle-") ? 0 : long.MaxValue;
    string file = upload.Headers.Location!.AbsolutePath.TrimStart('/');
    using HttpResponseMessage bundle = await _client.GetAsync($"/bundle.{extension}?files={file}");
    await AssertStorageFailureAsync(bundle);
    Assert.Empty(Directory.GetFiles(TempPath));
  }

  [Fact]
  public async Task EncryptedUpload_DecryptsWithGuardEnabledAsync()
  {
    using HttpRequestMessage request = CreateUpload("PUT", "/file.txt");
    request.Headers.Add("Encrypt-Password", "test-password");
    using HttpResponseMessage upload = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    using HttpRequestMessage download = new(HttpMethod.Get, upload.Headers.Location);
    download.Headers.Add("Decrypt-Password", "test-password");
    using HttpResponseMessage response = await _client.SendAsync(download);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(new byte[200_000], await response.Content.ReadAsByteArrayAsync());
    Assert.Empty(Directory.GetFiles(TempPath));
  }

  [Fact]
  public async Task DecryptedDownload_StopsAndCleansUpWhenSpaceRunsOutAsync()
  {
    using HttpRequestMessage request = CreateUpload("PUT", "/file.txt");
    request.Headers.Add("Encrypt-Password", "test-password");
    using HttpResponseMessage upload = await _client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    _probe.AvailableBytes = path => Path.GetFileName(path).StartsWith("decrypt-") ? 0 : long.MaxValue;
    using HttpRequestMessage download = new(HttpMethod.Get, upload.Headers.Location);
    download.Headers.Add("Decrypt-Password", "test-password");
    using HttpResponseMessage response = await _client.SendAsync(download);
    await AssertStorageFailureAsync(response);
    Assert.Empty(Directory.GetFiles(TempPath));
  }

  [Fact]
  public async Task Skill_ReportsConfiguredReserveAsync()
  {
    string skill = await _client.GetStringAsync("/SKILL.md");
    Assert.Contains("**Minimum free disk space:** 1 MiB", skill);
    Assert.Contains("507 Insufficient Storage", skill);
    Assert.DoesNotContain("{{MinFreeDiskSpace}}", skill);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(long.MaxValue)]
  public void InvalidReserve_IsRejectedAtStartup(long minimumMb)
  {
    using WebApplicationFactory<Program> invalid = _factory.WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => services.Configure<TransferCsOptions>(options =>
        options.MinFreeDiskSpaceMb = minimumMb)));
    Assert.Throws<OptionsValidationException>(() => invalid.CreateClient());
  }

  private static HttpRequestMessage CreateUpload(string method, string path)
  {
    ByteArrayContent file = new(new byte[200_000]);
    HttpContent body = file;
    if (method == "POST")
    {
      MultipartFormDataContent multipart = new();
      multipart.Add(file, "file", "file.txt");
      body = multipart;
    }
    return new HttpRequestMessage(new HttpMethod(method), path) { Content = body };
  }

  private static async Task AssertStorageFailureAsync(HttpResponseMessage response)
  {
    Assert.Equal(HttpStatusCode.InsufficientStorage, response.StatusCode);
    Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    Assert.Contains("not enough free space", await response.Content.ReadAsStringAsync());
  }

  private void AssertNoFiles()
  {
    if (Directory.Exists(_directory))
      Assert.Empty(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
  }

  public void Dispose()
  {
    _client.Dispose();
    _factory.Dispose();
    if (Directory.Exists(_directory))
      Directory.Delete(_directory, true);
  }
}
