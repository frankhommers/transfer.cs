using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TransferCs.Api.Models;
using TransferCs.Api.Tests.Helpers;
using static TransferCs.Api.Tests.Helpers.DownloadPasswordFixture;

namespace TransferCs.Api.Tests.Endpoints;

public class DownloadPasswordEndpointsTests(DownloadPasswordFixture fixture) : IClassFixture<DownloadPasswordFixture>
{
  private static readonly string[] _leakedHeaders = ["Repr-Digest", "Sunset", "X-Remaining-Downloads"];

  public static TheoryData<string> DownloadPaths => ["/{0}/file.txt", "/download/{0}/file.txt",
    "/get/{0}/file.txt", "/inline/{0}/file.txt"];

  [Fact]
  public async Task Upload_ReportsPasswordProtectedPerFileAsync()
  {
    using HttpClient client = fixture.CreateClient();

    (UploadedFile protectedFile, _) = await UploadAsync(client, UniqueToken("upload-protected"));
    (UploadedFile plainFile, _) = await UploadAsync(client, UniqueToken("upload-plain"), null);

    Assert.True(protectedFile.PasswordProtected);
    Assert.False(plainFile.PasswordProtected);
  }

  [Theory]
  [InlineData("PUT", "/put/file.txt")]
  [InlineData("PUT", "/upload/file.txt")]
  [InlineData("PUT", "/file.txt")]
  [InlineData("POST", "/")]
  [InlineData("POST", "/archive")]
  public async Task Upload_RejectsInvalidPasswordsOnEveryRouteAsync(string method, string path)
  {
    using HttpClient client = fixture.CreateClient();

    foreach (string[] values in new[] { [new string('a', 1025)], new[] { "one", "two" } })
    {
      using HttpRequestMessage request = UploadRequest(method, path);
      request.Headers.TryAddWithoutValidation("Download-Password", values);
      using HttpResponseMessage response = await client.SendAsync(request);

      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      Assert.Contains("Download-Password", await response.Content.ReadAsStringAsync());
    }
  }

  [Theory]
  [InlineData("PUT", "/put/file.txt")]
  [InlineData("PUT", "/upload/file.txt")]
  [InlineData("PUT", "/file.txt")]
  [InlineData("POST", "/")]
  [InlineData("POST", "/archive")]
  public async Task Upload_ProtectsFilesOnEveryRouteAsync(string method, string path)
  {
    using HttpClient client = fixture.CreateClient();
    string password = "a  b" + new string('p', 1020);
    using HttpRequestMessage request = UploadRequest(method, path);
    request.Headers.Add("Download-Password", password);

    using HttpResponseMessage response = await client.SendAsync(request);
    UploadResponse result = (await response.Content.ReadFromJsonAsync<UploadResponse>())!;

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.NotEmpty(result.Files);
    foreach (UploadedFile file in result.Files)
    {
      string filePath = new Uri(file.Url).AbsolutePath;
      Assert.True(file.PasswordProtected);
      using HttpResponseMessage denied = await SendAsync(client, HttpMethod.Get, filePath);
      using HttpResponseMessage trimmed = await SendAsync(client, HttpMethod.Get, filePath,
        password.Replace("  ", " "));
      using HttpResponseMessage allowed = await SendAsync(client, HttpMethod.Get, filePath, password);
      Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
      Assert.Equal(HttpStatusCode.Unauthorized, trimmed.StatusCode);
      Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }
  }

  [Theory]
  [MemberData(nameof(DownloadPaths))]
  public async Task Get_WithoutCredentials_Returns401WithoutLeakingMetadataAsync(string pathFormat)
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("get-denied");
    await UploadAsync(client, token);

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, string.Format(pathFormat, token));
    string body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
    Assert.Contains("Download-Password", body);
    Assert.DoesNotContain("protected content", body);
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    AssertNoLeakedHeaders(response);
  }

  [Theory]
  [MemberData(nameof(DownloadPaths))]
  public async Task Get_WithCorrectHeader_ReturnsFileAsync(string pathFormat)
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("get-allowed");
    await UploadAsync(client, token);

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, string.Format(pathFormat, token),
      Password);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("protected content", await response.Content.ReadAsStringAsync());
    Assert.True(response.Headers.Contains("Repr-Digest"));
  }

  [Theory]
  [MemberData(nameof(DownloadPaths))]
  public async Task Get_WithWrongHeader_Returns401Async(string pathFormat)
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("get-wrong");
    await UploadAsync(client, token);

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, string.Format(pathFormat, token),
      "wrong", accept: "text/html");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    AssertNoLeakedHeaders(response);
  }

  [Fact]
  public async Task Get_AfterMaxWrongAttempts_Returns429BeforeCheckingPasswordAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("get-limited");
    await UploadAsync(client, token);
    for (int attempt = 0; attempt < 3; attempt++)
    {
      using HttpResponseMessage wrong = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", "wrong");
      Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    using HttpResponseMessage limited = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", Password);
    using HttpResponseMessage limitedHead = await SendAsync(client, HttpMethod.Head, $"/{token}/file.txt", Password);

    Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    Assert.InRange(limited.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    Assert.Equal("no-store", limited.Headers.CacheControl!.ToString());
    AssertNoLeakedHeaders(limited);
    Assert.Equal(HttpStatusCode.TooManyRequests, limitedHead.StatusCode);
  }

  [Fact]
  public async Task Get_CorrectHeaderClearsFailedAttemptsAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("get-reset");
    await UploadAsync(client, token);
    for (int attempt = 0; attempt < 2; attempt++)
      (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", "wrong")).Dispose();
    (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", Password)).Dispose();
    for (int attempt = 0; attempt < 2; attempt++)
      (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", "wrong")).Dispose();

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", Password);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task BrowserGet_OnSharePath_ServesApplicationWithSiteTitleAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("browser-share");
    await UploadAsync(client, token);

    using HttpResponseMessage get = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt",
      accept: "text/html,application/xhtml+xml");
    using HttpResponseMessage head = await SendAsync(client, HttpMethod.Head, $"/{token}/file.txt",
      accept: "text/html");

    Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    Assert.Equal("text/html", get.Content.Headers.ContentType!.MediaType);
    Assert.Equal(Page.Replace("transfer.cs", "Alpha files"), await get.Content.ReadAsStringAsync());
    AssertNoLeakedHeaders(get);
    Assert.Equal(HttpStatusCode.OK, head.StatusCode);
    AssertNoLeakedHeaders(head);
  }

  [Theory]
  [InlineData("download")]
  [InlineData("get")]
  [InlineData("inline")]
  public async Task BrowserGet_OnActionPath_RedirectsToSharePageAsync(string action)
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("browser-action");
    await UploadAsync(client, token, filename: "my file.txt");

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/{action}/{token}/my%20file.txt",
      accept: "text/html");

    Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
    Assert.Equal($"http://alpha.test/{token}/my%20file.txt", response.Headers.Location!.OriginalString);
    AssertNoLeakedHeaders(response);
  }

  [Fact]
  public async Task Head_IsGatedWithoutLeakingMetadataAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("head");
    await UploadAsync(client, token);

    using HttpResponseMessage denied = await SendAsync(client, HttpMethod.Head, $"/{token}/file.txt");
    using HttpResponseMessage deniedAction = await SendAsync(client, HttpMethod.Head, $"/download/{token}/file.txt");
    using HttpResponseMessage allowed = await SendAsync(client, HttpMethod.Head, $"/{token}/file.txt", Password);

    Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    Assert.NotEqual("protected content".Length, denied.Content.Headers.ContentLength);
    AssertNoLeakedHeaders(denied);
    Assert.Equal(HttpStatusCode.Unauthorized, deniedAction.StatusCode);
    AssertNoLeakedHeaders(deniedAction);
    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    Assert.Equal("protected content".Length, allowed.Content.Headers.ContentLength);
    Assert.True(allowed.Headers.Contains("Repr-Digest"));
  }

  [Fact]
  public async Task DeniedRequests_DoNotConsumeDownloadsAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("counter");
    (_, string adminToken) = await UploadAsync(client, token, maxDownloads: 1);

    (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt")).Dispose();
    (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", "wrong")).Dispose();
    (await SendAsync(client, HttpMethod.Get, $"/download/{token}/file.txt", accept: "text/html")).Dispose();
    (await SendAsync(client, HttpMethod.Get, $"/bundle.zip?files={token}/file.txt")).Dispose();
    using JsonDocument before = await GetAdminMetadataAsync(client, token, adminToken);
    using HttpResponseMessage allowed = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", Password);
    using HttpResponseMessage exhausted = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", Password);

    Assert.Equal(0, before.RootElement.GetProperty("downloads").GetInt32());
    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
  }

  [Fact]
  public async Task PasswordHeader_IsIgnoredForUnprotectedFilesAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("plain");
    await UploadAsync(client, token, null);

    using HttpResponseMessage withHeader = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", "anything");
    using HttpResponseMessage browser = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt",
      accept: "text/html");

    Assert.Equal(HttpStatusCode.OK, withHeader.StatusCode);
    Assert.Equal("protected content", await withHeader.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.OK, browser.StatusCode);
    Assert.Equal("protected content", await browser.Content.ReadAsStringAsync());
  }

  [Theory]
  [InlineData("bundle.zip")]
  [InlineData("bundle.tar")]
  [InlineData("bundle.tar.gz")]
  public async Task Bundle_RequiresAuthorizationForEveryProtectedFileAsync(string bundle)
  {
    using HttpClient client = fixture.CreateClient();
    string plainToken = UniqueToken("bundle-plain");
    string protectedToken = UniqueToken("bundle-protected");
    (_, string plainAdmin) = await UploadAsync(client, plainToken, null);
    await UploadAsync(client, protectedToken);
    string path = $"/{bundle}?files={plainToken}/file.txt,{protectedToken}/file.txt";

    using HttpResponseMessage denied = await SendAsync(client, HttpMethod.Get, path);
    using HttpResponseMessage wrong = await SendAsync(client, HttpMethod.Get, path, "wrong");
    using JsonDocument afterDenied = await GetAdminMetadataAsync(client, plainToken, plainAdmin);
    using HttpResponseMessage allowed = await SendAsync(client, HttpMethod.Get, path, Password);

    Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    Assert.Equal(0, afterDenied.RootElement.GetProperty("downloads").GetInt32());
    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
  }

  [Fact]
  public async Task Bundle_AcceptsUnlockCookieAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("bundle-cookie");
    await UploadAsync(client, token);
    using HttpResponseMessage unlock = await UnlockAsync(client, token, "file.txt", Password);

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/bundle.zip?files={token}/file.txt",
      cookie: CookieHeader(unlock));
    using ZipArchive archive = new(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("file.txt", Assert.Single(archive.Entries).FullName);
  }

  [Fact]
  public async Task Preview_WhileLocked_ReturnsOnlyPublicFieldsAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("preview-locked");
    await UploadAsync(client, token);

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/preview/{token}/file.txt");
    using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(["filename", "url", "downloadUrl", "token", "hostname", "qrCode", "passwordProtected"],
      document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    Assert.True(document.RootElement.GetProperty("passwordProtected").GetBoolean());
    Assert.Equal($"http://alpha.test/{token}/file.txt", document.RootElement.GetProperty("url").GetString());
  }

  [Fact]
  public async Task Preview_WhenAuthorized_ReturnsFullPreviewAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("preview-unlocked");
    await UploadAsync(client, token);
    using HttpResponseMessage unlock = await UnlockAsync(client, token, "file.txt", Password);

    using HttpResponseMessage viaCookie = await SendAsync(client, HttpMethod.Get, $"/api/preview/{token}/file.txt",
      cookie: CookieHeader(unlock));
    using HttpResponseMessage viaHeader = await SendAsync(client, HttpMethod.Get, $"/api/preview/{token}/file.txt",
      Password);
    using HttpResponseMessage wrong = await SendAsync(client, HttpMethod.Get, $"/api/preview/{token}/file.txt",
      "wrong");
    using JsonDocument document = JsonDocument.Parse(await viaCookie.Content.ReadAsStringAsync());

    Assert.Equal(HttpStatusCode.OK, viaCookie.StatusCode);
    Assert.Equal("text", document.RootElement.GetProperty("previewType").GetString());
    Assert.Equal("protected content".Length, document.RootElement.GetProperty("contentLength").GetInt64());
    Assert.True(document.RootElement.GetProperty("passwordProtected").GetBoolean());
    Assert.Equal(HttpStatusCode.OK, viaHeader.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
  }

  [Fact]
  public async Task Preview_ForUnprotectedFile_ReportsNotProtectedAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("preview-plain");
    await UploadAsync(client, token, null);

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/preview/{token}/file.txt");
    using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    Assert.False(document.RootElement.GetProperty("passwordProtected").GetBoolean());
    Assert.Equal("text", document.RootElement.GetProperty("previewType").GetString());
  }

  [Fact]
  public async Task AdminMetadata_ReportsPasswordProtectionWithoutRequiringPasswordAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string protectedToken = UniqueToken("admin-protected");
    string plainToken = UniqueToken("admin-plain");
    (_, string protectedAdmin) = await UploadAsync(client, protectedToken);
    (_, string plainAdmin) = await UploadAsync(client, plainToken, null);

    using JsonDocument protectedMetadata = await GetAdminMetadataAsync(client, protectedToken, protectedAdmin);
    using JsonDocument plainMetadata = await GetAdminMetadataAsync(client, plainToken, plainAdmin);

    Assert.True(protectedMetadata.RootElement.GetProperty("passwordProtected").GetBoolean());
    Assert.False(plainMetadata.RootElement.GetProperty("passwordProtected").GetBoolean());
    Assert.False(protectedMetadata.RootElement.TryGetProperty("passwordHash", out _));
  }

  [Fact]
  public async Task DeletionToken_IsNotGatedAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("delete");
    (UploadedFile file, _) = await UploadAsync(client, token);

    using HttpResponseMessage response = await client.DeleteAsync(new Uri(file.DeleteUrl).AbsolutePath);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Theory]
  [InlineData("PUT", "/put/file.txt")]
  [InlineData("POST", "/")]
  [InlineData("POST", "/archive")]
  public async Task Upload_AcceptsAnyPasswordThroughBase64HeaderAsync(string method, string path)
  {
    using HttpClient client = fixture.CreateClient();
    const string password = " café ☕ 1lI0O \"'$<>& ";
    using HttpRequestMessage request = UploadRequest(method, path);
    request.Headers.Add("Download-Password-Base64", Convert.ToBase64String(Encoding.UTF8.GetBytes(password)));

    using HttpResponseMessage response = await client.SendAsync(request);
    UploadResponse result = (await response.Content.ReadFromJsonAsync<UploadResponse>())!;

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    foreach (UploadedFile file in result.Files)
    {
      string[] segments = new Uri(file.Url).AbsolutePath.Trim('/').Split('/');
      using HttpResponseMessage trimmed = await UnlockAsync(client, segments[0], segments[1], password.Trim());
      using HttpResponseMessage unlocked = await UnlockAsync(client, segments[0], segments[1], password);
      Assert.Equal(HttpStatusCode.Unauthorized, trimmed.StatusCode);
      Assert.Equal(HttpStatusCode.NoContent, unlocked.StatusCode);
    }
  }

  [Fact]
  public async Task Get_AcceptsBase64PasswordHeaderAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("base64-get");
    await UploadAsync(client, token);
    using HttpRequestMessage request = new(HttpMethod.Get, $"/{token}/file.txt");
    request.Headers.Add("Download-Password-Base64", Convert.ToBase64String(Encoding.UTF8.GetBytes(Password)));

    using HttpResponseMessage response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Theory]
  [InlineData("PUT", "/put/file.txt")]
  [InlineData("POST", "/archive")]
  public async Task Upload_RejectsBothPasswordHeadersAndInvalidBase64Async(string method, string path)
  {
    using HttpClient client = fixture.CreateClient();

    using HttpRequestMessage both = UploadRequest(method, path);
    both.Headers.Add("Download-Password", "x");
    both.Headers.Add("Download-Password-Base64", "eA==");
    using HttpRequestMessage invalid = UploadRequest(method, path);
    invalid.Headers.Add("Download-Password-Base64", "not base64!");
    using HttpResponseMessage bothResponse = await client.SendAsync(both);
    using HttpResponseMessage invalidResponse = await client.SendAsync(invalid);

    Assert.Equal(HttpStatusCode.BadRequest, bothResponse.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
  }

  private static HttpRequestMessage UploadRequest(string method, string path)
  {
    HttpContent content;
    if (method == "POST")
    {
      MultipartFormDataContent multipart = new();
      multipart.Add(new StringContent("first"), "file", "first.txt");
      multipart.Add(new StringContent("second"), "file", "second.txt");
      content = multipart;
    }
    else
    {
      content = new StringContent("content");
    }

    HttpRequestMessage request = new(new HttpMethod(method), path) { Content = content };
    request.Headers.Accept.ParseAdd("application/json");
    return request;
  }

  private static void AssertNoLeakedHeaders(HttpResponseMessage response)
  {
    foreach (string header in _leakedHeaders)
      Assert.False(response.Headers.Contains(header), $"{header} must not be sent");
    Assert.Null(response.Content.Headers.ContentDisposition);
  }
}
