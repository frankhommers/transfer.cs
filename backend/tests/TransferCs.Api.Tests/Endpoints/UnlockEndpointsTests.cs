using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using TransferCs.Api.Services;
using TransferCs.Api.Tests.Helpers;
using static TransferCs.Api.Tests.Helpers.DownloadPasswordFixture;

namespace TransferCs.Api.Tests.Endpoints;

public class UnlockEndpointsTests(DownloadPasswordFixture fixture) : IClassFixture<DownloadPasswordFixture>
{
  [Fact]
  public async Task Unlock_WithCorrectPassword_SetsScopedCookieAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("unlock");
    await UploadAsync(client, token);

    using HttpResponseMessage response = await UnlockAsync(client, token, "file.txt", Password);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    SetCookieHeaderValue cookie = SetCookieHeaderValue.Parse(Assert.Single(response.Headers.GetValues("Set-Cookie")));
    Assert.Equal(UnlockCookie.GetName(token, "file.txt"), cookie.Name.ToString());
    Assert.True(cookie.HttpOnly);
    Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
    Assert.Equal("/", cookie.Path.ToString());
    Assert.False(cookie.Secure);
    Assert.InRange(cookie.Expires!.Value, DateTimeOffset.UtcNow.AddHours(12).AddMinutes(-1),
      DateTimeOffset.UtcNow.AddHours(12).AddMinutes(1));
  }

  [Fact]
  public async Task Unlock_OverHttps_SetsSecureCookieAsync()
  {
    using HttpClient client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
      BaseAddress = new Uri("https://alpha.test"),
      HandleCookies = false
    });
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
      Convert.ToBase64String("user:password"u8.ToArray()));
    string token = UniqueToken("unlock-https");
    await UploadAsync(client, token);

    using HttpResponseMessage response = await UnlockAsync(client, token, "file.txt", Password);

    Assert.True(SetCookieHeaderValue.Parse(Assert.Single(response.Headers.GetValues("Set-Cookie"))).Secure);
  }

  [Fact]
  public async Task UnlockCookie_GrantsGetRangeAndInlineAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("unlock-cookie");
    await UploadAsync(client, token);
    using HttpResponseMessage unlock = await UnlockAsync(client, token, "file.txt", Password);
    string cookie = CookieHeader(unlock);

    using HttpResponseMessage get = await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", cookie: cookie,
      accept: "text/html");
    using HttpRequestMessage rangeRequest = new(HttpMethod.Get, $"/download/{token}/file.txt");
    rangeRequest.Headers.Add("Cookie", cookie);
    rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 8);
    using HttpResponseMessage range = await client.SendAsync(rangeRequest);
    using HttpResponseMessage inline = await SendAsync(client, HttpMethod.Get, $"/inline/{token}/file.txt",
      cookie: cookie);
    using HttpResponseMessage head = await SendAsync(client, HttpMethod.Head, $"/{token}/file.txt", cookie: cookie);

    Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    Assert.Equal("protected content", await get.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
    Assert.Equal("protected", await range.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.OK, inline.StatusCode);
    Assert.Equal("inline", inline.Content.Headers.ContentDisposition!.DispositionType);
    Assert.Equal(HttpStatusCode.OK, head.StatusCode);
  }

  [Fact]
  public async Task UnlockCookie_IsScopedToOneFileAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("unlock-scope");
    string otherToken = UniqueToken("unlock-other");
    await UploadAsync(client, token);
    await UploadAsync(client, otherToken);
    using HttpResponseMessage unlock = await UnlockAsync(client, token, "file.txt", Password);
    string value = CookieHeader(unlock)[(CookieHeader(unlock).IndexOf('=') + 1)..];

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/{otherToken}/file.txt",
      cookie: $"{UnlockCookie.GetName(otherToken, "file.txt")}={value}");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Unlock_WithWrongPassword_Returns401WithoutCookieAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("unlock-wrong");
    await UploadAsync(client, token);

    using HttpResponseMessage response = await UnlockAsync(client, token, "file.txt", "wrong");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.False(response.Headers.Contains("Set-Cookie"));
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
  }

  [Fact]
  public async Task Unlock_AfterMaxAttempts_Returns429AndSharesLimitWithHeaderAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("unlock-limit");
    await UploadAsync(client, token);
    (await UnlockAsync(client, token, "file.txt", "wrong")).Dispose();
    (await UnlockAsync(client, token, "file.txt", "wrong")).Dispose();
    (await SendAsync(client, HttpMethod.Get, $"/{token}/file.txt", "wrong")).Dispose();

    using HttpResponseMessage response = await UnlockAsync(client, token, "file.txt", Password);

    Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    Assert.NotNull(response.Headers.RetryAfter!.Delta);
    Assert.False(response.Headers.Contains("Set-Cookie"));
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
  }

  [Fact]
  public async Task Unlock_MissingFile_Returns404Async()
  {
    using HttpClient client = fixture.CreateClient();

    using HttpResponseMessage response = await UnlockAsync(client, UniqueToken("missing"), "file.txt", Password);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
  }

  [Fact]
  public async Task Unlock_UnprotectedFile_Returns204WithoutCookieAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("unlock-plain");
    await UploadAsync(client, token, null);

    using HttpResponseMessage response = await UnlockAsync(client, token, "file.txt", "anything");

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.False(response.Headers.Contains("Set-Cookie"));
  }

  [Theory]
  [InlineData("{}")]
  [InlineData("{\"password\":null}")]
  [InlineData("{\"password\":\"\"}")]
  public async Task Unlock_WithoutPassword_Returns400Async(string json)
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("unlock-empty");
    await UploadAsync(client, token);
    using HttpRequestMessage request = new(HttpMethod.Post, $"/api/unlock/{token}/file.txt")
    {
      Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    using HttpResponseMessage response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Unlock_DoesNotRequireUploadBasicAuthAsync()
  {
    using HttpClient client = fixture.CreateClient();
    string token = UniqueToken("unlock-basic");
    await UploadAsync(client, token);
    client.DefaultRequestHeaders.Authorization = null;
    using StringContent upload = new("content");

    using HttpResponseMessage unlock = await UnlockAsync(client, token, "file.txt", Password);
    using HttpResponseMessage put = await client.PutAsync("/put/file.txt", upload);

    Assert.Equal(HttpStatusCode.NoContent, unlock.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
  }

  [Fact]
  public async Task SiteOverrides_ApplyLimitsAndCookieLifetimePerSiteAsync()
  {
    using HttpClient alpha = fixture.CreateClient();
    using HttpClient beta = fixture.CreateClient("beta.test");
    string token = UniqueToken("site-limits");
    await UploadAsync(alpha, token);
    await UploadAsync(beta, token);

    (await UnlockAsync(beta, token, "file.txt", "wrong")).Dispose();
    using HttpResponseMessage betaLimited = await UnlockAsync(beta, token, "file.txt", Password);
    (await UnlockAsync(alpha, token, "file.txt", "wrong")).Dispose();
    using HttpResponseMessage alphaUnlock = await UnlockAsync(alpha, token, "file.txt", Password);

    Assert.Equal(HttpStatusCode.TooManyRequests, betaLimited.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, alphaUnlock.StatusCode);

    string betaToken = UniqueToken("site-cookie");
    await UploadAsync(beta, betaToken);
    using HttpResponseMessage betaUnlock = await UnlockAsync(beta, betaToken, "file.txt", Password);
    SetCookieHeaderValue cookie = SetCookieHeaderValue.Parse(Assert.Single(betaUnlock.Headers.GetValues("Set-Cookie")));
    Assert.InRange(cookie.Expires!.Value, DateTimeOffset.UtcNow.AddHours(2).AddMinutes(-1),
      DateTimeOffset.UtcNow.AddHours(2).AddMinutes(1));
  }

  [Theory]
  [InlineData("TransferCs:DownloadPasswordMaxAttempts", "-1")]
  [InlineData("TransferCs:DownloadPasswordAttemptWindowMinutes", "0")]
  [InlineData("TransferCs:DownloadPasswordUnlockHours", "0")]
  [InlineData("TransferCs:Sites:alpha:DownloadPasswordUnlockHours", "0")]
  public void InvalidSettings_AreRejectedAtStartup(string key, string value)
  {
    using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["TransferCs:InitialSiteId"] = "alpha",
          ["TransferCs:Sites:alpha:Hosts:0"] = "alpha.test",
          [key] = value
        })));

    Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
  }
}
