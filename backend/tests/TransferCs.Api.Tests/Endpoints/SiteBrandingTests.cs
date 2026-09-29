using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TransferCs.Api.Tests.Endpoints;

public class SiteBrandingTests : IAsyncLifetime
{
  private const string Page =
    "<!doctype html><html><head><title>transfer.cs</title></head><body><div id=\"root\"></div></body></html>";

  private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"transfer-branding-{Guid.NewGuid():N}");
  private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"transfer-branding-data-{Guid.NewGuid():N}");
  private WebApplicationFactory<Program> _factory = null!;

  public async Task InitializeAsync()
  {
    Directory.CreateDirectory(_webRoot);
    await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), Page);
    _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
      .UseWebRoot(_webRoot)
      .ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["TransferCs:Title"] = "Global files",
          ["TransferCs:InitialSiteId"] = "alpha",
          ["TransferCs:BasePath"] = _basePath,
          ["TransferCs:HttpAuthUser"] = "user",
          ["TransferCs:HttpAuthPass"] = "password",
          ["TransferCs:Sites:alpha:Hosts:0"] = "alpha.test",
          ["TransferCs:Sites:alpha:Title"] = "Alpha files",
          ["TransferCs:Sites:beta:Hosts:0"] = "beta.test",
          ["TransferCs:Sites:beta:Title"] = "Beta files",
          ["TransferCs:Sites:evil:Hosts:0"] = "evil.test",
          ["TransferCs:Sites:evil:Title"] = "<script>alert(\"x\")</script> \\ Café\r\nX-Injected: 1"
        })));
  }

  public async Task DisposeAsync()
  {
    await _factory.DisposeAsync();
    Directory.Delete(_webRoot, true);
    if (Directory.Exists(_basePath))
      Directory.Delete(_basePath, true);
  }

  [Theory]
  [InlineData("/")]
  [InlineData("/index.html")]
  [InlineData("/admin/missing-token/file.txt")]
  [InlineData("/unknown-page")]
  [InlineData("/a/b/c/d")]
  public async Task SpaRoutes_InjectResolvedSiteTitleAsync(string path)
  {
    using HttpClient client = _factory.CreateClient();

    using HttpResponseMessage alpha = await SendAsync(client, HttpMethod.Get, path, "alpha.test");
    using HttpResponseMessage beta = await SendAsync(client, HttpMethod.Get, path, "beta.test");

    Assert.Equal(HttpStatusCode.OK, alpha.StatusCode);
    Assert.Equal("text/html", alpha.Content.Headers.ContentType!.MediaType);
    Assert.Equal("utf-8", alpha.Content.Headers.ContentType.CharSet);
    Assert.Equal(Page.Replace("transfer.cs", "Alpha files"), await alpha.Content.ReadAsStringAsync());
    Assert.Equal(Page.Replace("transfer.cs", "Beta files"), await beta.Content.ReadAsStringAsync());
  }

  [Theory]
  [InlineData("/")]
  [InlineData("/admin/missing-token/file.txt")]
  [InlineData("/unknown-page")]
  public async Task SpaRoutes_HtmlEncodeSiteTitleAsync(string path)
  {
    using HttpClient client = _factory.CreateClient();

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, path, "evil.test");
    string html = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.DoesNotContain("<script>", html);
    Assert.Contains("<title>&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; \\ Caf&#233;\r\nX-Injected: 1</title>", html);
  }

  [Fact]
  public async Task SpaHead_ReturnsInjectedLengthWithoutBodyAsync()
  {
    using HttpClient client = _factory.CreateClient();

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Head, "/admin/token/file.txt", "beta.test");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(Page.Replace("transfer.cs", "Beta files").Length, response.Content.Headers.ContentLength);
    Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
  }

  [Fact]
  public async Task Fallback_DoesNotServeApplicationForUnsafeMethodsAsync()
  {
    using HttpClient client = _factory.CreateClient();

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, "/a/b/c/d", "alpha.test");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Theory]
  [InlineData("alpha.test", "Basic realm=\"Alpha files\"")]
  [InlineData("beta.test", "Basic realm=\"Beta files\"")]
  [InlineData("evil.test", "Basic realm=\"<script>alert(\\\"x\\\")</script> \\\\ CafeX-Injected: 1\"")]
  public async Task BasicAuthRealm_UsesResolvedSiteTitleAsync(string host, string expected)
  {
    using HttpClient client = _factory.CreateClient();

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Put, "/file.txt", host, "content");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Equal(expected, response.Headers.NonValidated["WWW-Authenticate"].ToString());
  }

  [Theory]
  [InlineData("alpha.test", "Alpha files")]
  [InlineData("beta.test", "Beta files")]
  [InlineData("evil.test", "<script>alert(\"x\")</script> \\ CafeX-Injected: 1")]
  public async Task ServerHeaders_UseResolvedSiteTitleAsync(string host, string expected)
  {
    using HttpClient client = _factory.CreateClient();

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/config", host);

    Assert.Equal(expected, response.Headers.NonValidated["Server"].ToString());
    Assert.Equal(expected, response.Headers.NonValidated["x-served-by"].ToString());
    Assert.False(response.Headers.Contains("X-Injected"));
    Assert.Equal("<3 inspired by transfer.sh", response.Headers.GetValues("x-made-with").Single());
  }

  [Fact]
  public async Task ServerHeaders_UseGlobalTitleWithoutResolvedSiteAsync()
  {
    using HttpClient client = _factory.CreateClient();

    using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/health", "unknown.test");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("Global files", response.Headers.NonValidated["Server"].ToString());
  }

  private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
    string host, string? content = null)
  {
    using HttpRequestMessage request = new(method, path);
    request.Headers.Host = host;
    request.Headers.Accept.ParseAdd("text/html");
    if (content != null)
    {
      request.Content = new StringContent(content);
      request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
    }
    return await client.SendAsync(request);
  }
}
