using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TransferCs.Api.Models;
using TransferCs.Api.Services;
using TransferCs.Api.Tests.Helpers;
using static TransferCs.Api.Tests.Helpers.DownloadPasswordFixture;

namespace TransferCs.Api.Tests.Endpoints;

public class AdminPasswordEndpointsTests(DownloadPasswordFixture fixture) : IClassFixture<DownloadPasswordFixture>
{
  private const string NewPassword = "new  secret";
  private const string UnicodePassword = " ✓ spaced ";

  [Fact]
  public async Task SetPassword_ProtectsPreviouslyUnprotectedFileAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-set");
    (_, string adminToken) = await UploadAsync(client, token, null);

    using HttpResponseMessage response = await SetAdminPasswordAsync(client, token, adminToken, UnicodePassword);
    using HttpResponseMessage denied = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt");
    using HttpResponseMessage trimmed = await UnlockAsync(client, token, "file.txt", UnicodePassword.Trim());
    using HttpResponseMessage unlock = await UnlockAsync(client, token, "file.txt", UnicodePassword);
    using JsonDocument metadata = await GetAdminMetadataAsync(client, token, adminToken);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, trimmed.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, unlock.StatusCode);
    Assert.True(unlock.Headers.Contains("Set-Cookie"));
    Assert.True(metadata.RootElement.GetProperty("passwordProtected").GetBoolean());
  }

  [Fact]
  public async Task ChangePassword_ReplacesPasswordAndInvalidatesUnlockCookiesAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-change");
    (_, string adminToken) = await UploadAsync(client, token);
    using HttpResponseMessage unlock = await UnlockAsync(client, token, "file.txt", Password);
    string cookie = CookieHeader(unlock);
    using HttpResponseMessage beforeChange = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt",
      cookie: cookie);

    using HttpResponseMessage response = await SetAdminPasswordAsync(client, token, adminToken, NewPassword);
    using HttpResponseMessage oldCookie = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt",
      cookie: cookie);
    using HttpResponseMessage oldPassword = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", Password);
    using HttpResponseMessage newPassword = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt",
      NewPassword);

    Assert.Equal(HttpStatusCode.OK, beforeChange.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, oldCookie.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
    Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    Assert.Equal("protected content", await newPassword.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task SetSamePassword_UsesNewSaltAndInvalidatesUnlockCookiesAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-same");
    (_, string adminToken) = await UploadAsync(client, token);
    using HttpResponseMessage unlock = await UnlockAsync(client, token, "file.txt", Password);
    string cookie = CookieHeader(unlock);

    using HttpResponseMessage response = await SetAdminPasswordAsync(client, token, adminToken, Password);
    using HttpResponseMessage oldCookie = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt",
      cookie: cookie);
    using HttpResponseMessage samePassword = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", Password);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, oldCookie.StatusCode);
    Assert.Equal(HttpStatusCode.OK, samePassword.StatusCode);
  }

  [Fact]
  public async Task RemovePassword_MakesFileFreelyDownloadableAndIsIdempotentAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-remove");
    (_, string adminToken) = await UploadAsync(client, token);

    using HttpResponseMessage response = await RemoveAdminPasswordAsync(client, token, adminToken);
    using HttpResponseMessage again = await RemoveAdminPasswordAsync(client, token, adminToken);
    using HttpResponseMessage download = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt");
    using JsonDocument metadata = await GetAdminMetadataAsync(client, token, adminToken);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    Assert.Equal("protected content", await download.Content.ReadAsStringAsync());
    Assert.False(metadata.RootElement.GetProperty("passwordProtected").GetBoolean());
  }

  [Fact]
  public async Task PasswordChanges_KeepDownloadCountsAndTokensAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-keep");
    (UploadedFile file, string adminToken) = await UploadAsync(client, token, null, maxDownloads: 5);
    (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt")).Dispose();

    (await SetAdminPasswordAsync(client, token, adminToken, NewPassword)).Dispose();
    (await RemoveAdminPasswordAsync(client, token, adminToken)).Dispose();
    using JsonDocument metadata = await GetAdminMetadataAsync(client, token, adminToken);
    using HttpResponseMessage delete = await client.DeleteAsync(new Uri(file.DeleteUrl).AbsolutePath);

    Assert.Equal(1, metadata.RootElement.GetProperty("downloads").GetInt32());
    Assert.Equal(5, metadata.RootElement.GetProperty("maxDownloads").GetInt32());
    Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
  }

  [Fact]
  public async Task SetPassword_ResetsAttemptLimiterAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-limit-set");
    (_, string adminToken) = await UploadAsync(client, token);
    for (int attempt = 0; attempt < 3; attempt++)
      (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", "wrong")).Dispose();
    using HttpResponseMessage limited = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", Password);

    using HttpResponseMessage response = await SetAdminPasswordAsync(client, token, adminToken, NewPassword);
    using HttpResponseMessage allowed = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", NewPassword);

    Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
  }

  [Fact]
  public async Task RemovePassword_ResetsAttemptLimiterAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-limit-remove");
    (_, string adminToken) = await UploadAsync(client, token);
    for (int attempt = 0; attempt < 3; attempt++)
      (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", "wrong")).Dispose();
    DownloadPasswordAttemptLimiter limiter = fixture.Factory.Services.GetRequiredService<DownloadPasswordAttemptLimiter>();

    using HttpResponseMessage response = await RemoveAdminPasswordAsync(client, token, adminToken);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.True(limiter.TryBeginAttempt("alpha", token, "file.txt", 1, TimeSpan.FromMinutes(15), out _));
  }

  [Theory]
  [InlineData("{}")]
  [InlineData("{\"password\":null}")]
  [InlineData("{\"password\":\"\"}")]
  [InlineData("{\"password\":")]
  [InlineData("[]")]
  public async Task SetPassword_WithInvalidBody_Returns400Async(string json)
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-invalid");
    (_, string adminToken) = await UploadAsync(client, token, null);

    using HttpResponseMessage response = await SetAdminPasswordJsonAsync(client, token, adminToken, json);
    using HttpResponseMessage download = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    Assert.Equal(HttpStatusCode.OK, download.StatusCode);
  }

  [Fact]
  public async Task SetPassword_AcceptsMaximumLengthAndRejectsLongerAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-length");
    (_, string adminToken) = await UploadAsync(client, token, null);
    string longest = new('p', 1024);

    using HttpResponseMessage tooLong = await SetAdminPasswordAsync(client, token, adminToken, longest + "p");
    using HttpResponseMessage accepted = await SetAdminPasswordAsync(client, token, adminToken, longest);
    using HttpResponseMessage download = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", longest);

    Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    Assert.Equal(HttpStatusCode.OK, download.StatusCode);
  }

  [Fact]
  public async Task SetPassword_WithoutJsonContentType_Returns415Async()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-media");
    (_, string adminToken) = await UploadAsync(client, token, null);

    using HttpResponseMessage response = await SetAdminPasswordJsonAsync(client, token, adminToken,
      "{\"password\":\"secret\"}", "text/plain");

    Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("wrong")]
  public async Task PasswordRoutes_WithoutValidAdminToken_Return404Async(string? adminToken)
  {
    using HttpClient client = fixture.CreateClient();
    string plainToken = UniqueToken("admin-denied-plain");
    string protectedToken = UniqueToken("admin-denied-protected");
    await UploadAsync(client, plainToken, null);
    await UploadAsync(client, protectedToken);

    using HttpResponseMessage set = await SetAdminPasswordJsonAsync(client, plainToken, adminToken,
      "{\"password\":\"secret\"}");
    using HttpResponseMessage setWrongType = await SetAdminPasswordJsonAsync(client, plainToken, adminToken,
      "not json", "text/plain");
    using HttpResponseMessage remove = await RemoveAdminPasswordAsync(client, protectedToken, adminToken);
    using HttpResponseMessage plainDownload = await SendAsync(client, HttpMethod.Get, $"/{plainToken}/file.txt");
    using HttpResponseMessage protectedDownload = await SendAsync(client, HttpMethod.Get,
      $"/{protectedToken}/file.txt");

    Assert.Equal(HttpStatusCode.NotFound, set.StatusCode);
    Assert.Equal("no-store", set.Headers.CacheControl!.ToString());
    Assert.Equal(HttpStatusCode.NotFound, setWrongType.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
    Assert.Equal(HttpStatusCode.OK, plainDownload.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, protectedDownload.StatusCode);
  }

  [Fact]
  public async Task PasswordRoutes_ForMissingFile_Return404Async()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-missing");
    (_, string adminToken) = await UploadAsync(client, token, null);

    using HttpResponseMessage set = await SetAdminPasswordAsync(client, token, adminToken, NewPassword,
      "other.txt");
    using HttpResponseMessage remove = await RemoveAdminPasswordAsync(client, token, adminToken, "other.txt");

    Assert.Equal(HttpStatusCode.NotFound, set.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
  }

  [Fact]
  public async Task PasswordRoutes_DoNotRequireUploadBasicAuthAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("admin-basic");
    (_, string adminToken) = await UploadAsync(client, token, null);
    client.DefaultRequestHeaders.Authorization = null;

    using HttpResponseMessage set = await SetAdminPasswordAsync(client, token, adminToken, NewPassword);
    using HttpResponseMessage remove = await RemoveAdminPasswordAsync(client, token, adminToken);
    using HttpResponseMessage anonymous = await RemoveAdminPasswordAsync(client, token, null);

    Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
    Assert.False(anonymous.Headers.Contains("WWW-Authenticate"));
  }

  [Fact]
  public async Task PasswordRoutes_AreIsolatedPerSiteAsync()
  {
    using HttpClient alpha = fixture.CreateClient();
    using HttpClient beta = fixture.CreateClient("beta.test");
    string token = UniqueToken("admin-site");
    (_, string alphaAdmin) = await UploadAsync(alpha, token, null);
    (_, string betaAdmin) = await UploadAsync(beta, token);

    using HttpResponseMessage crossSet = await SetAdminPasswordAsync(beta, token, alphaAdmin, NewPassword);
    using HttpResponseMessage crossRemove = await RemoveAdminPasswordAsync(beta, token, alphaAdmin);
    using HttpResponseMessage alphaSet = await SetAdminPasswordAsync(alpha, token, alphaAdmin, NewPassword);
    using HttpResponseMessage betaDownload = await SendAsync(beta, HttpMethod.Get, $"/{token}/file.txt", Password);
    using HttpResponseMessage betaNewPassword = await SendAsync(beta, HttpMethod.Get, $"/{token}/file.txt",
      NewPassword);
    using HttpResponseMessage alphaDownload = await SendAsync(alpha, HttpMethod.Get, $"/{token}/file.txt",
      NewPassword);

    Assert.NotEqual(alphaAdmin, betaAdmin);
    Assert.Equal(HttpStatusCode.NotFound, crossSet.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, crossRemove.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, alphaSet.StatusCode);
    Assert.Equal(HttpStatusCode.OK, betaDownload.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, betaNewPassword.StatusCode);
    Assert.Equal(HttpStatusCode.OK, alphaDownload.StatusCode);
  }
}
