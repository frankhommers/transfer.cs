using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TransferCs.Api.Tests.Endpoints;

public class MultiSiteEndpointsTests
{
  [Fact]
  public async Task UnknownHost_Returns421_ButHealthRemainsAvailable()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();

    using HttpResponseMessage unknown = await SendAsync(client, HttpMethod.Get, "/api/config", "unknown.test");
    using HttpResponseMessage health = await SendAsync(client, HttpMethod.Get, "/health", "unknown.test");
    using HttpResponseMessage nestedHealth = await SendAsync(client, HttpMethod.Get, "/health/details", "unknown.test");

    Assert.Equal((HttpStatusCode)421, unknown.StatusCode);
    Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    Assert.Equal((HttpStatusCode)421, nestedHealth.StatusCode);
  }

  [Fact]
  public async Task ConfigAndUpload_UseResolvedSiteBrandingAndBaseUrl()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();

    using HttpResponseMessage configResponse = await SendAsync(client, HttpMethod.Get, "/api/config", "alpha.test");
    using JsonDocument config = JsonDocument.Parse(await configResponse.Content.ReadAsStringAsync());
    using HttpResponseMessage upload = await SendAsync(client, HttpMethod.Put, "/put/file.txt", "alpha.test", "site data");

    Assert.Equal(HttpStatusCode.OK, configResponse.StatusCode);
    Assert.Equal("Alpha files", config.RootElement.GetProperty("title").GetString());
    Assert.StartsWith("https://cdn.alpha.test/", await upload.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task IdenticalTokenAndFilename_AreIsolatedByHost()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();
    const string token = "same-custom-token";

    using HttpResponseMessage alphaUpload = await SendAsync(
      client, HttpMethod.Put, "/put/file.txt", "alpha.test", "alpha", token);
    using HttpResponseMessage betaUpload = await SendAsync(
      client, HttpMethod.Put, "/put/file.txt", "beta.test", "beta", token);
    using HttpResponseMessage alphaDownload = await SendAsync(
      client, HttpMethod.Get, $"/{token}/file.txt", "alpha.test");
    using HttpResponseMessage betaDownload = await SendAsync(
      client, HttpMethod.Get, $"/{token}/file.txt", "beta.test");

    Assert.Equal(HttpStatusCode.OK, alphaUpload.StatusCode);
    Assert.Equal(HttpStatusCode.OK, betaUpload.StatusCode);
    Assert.Equal("alpha", await alphaDownload.Content.ReadAsStringAsync());
    Assert.Equal("beta", await betaDownload.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Bundle_PathTraversalIsRejected()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory();
    using HttpClient client = factory.CreateClient();

    using HttpResponseMessage response = await SendAsync(
      client, HttpMethod.Get, "/bundle.zip?files=../beta/token/file.txt", "alpha.test");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private static WebApplicationFactory<Program> CreateFactory() =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["TransferCs:InitialSiteId"] = "alpha",
          ["TransferCs:BasePath"] = Path.Combine(Path.GetTempPath(), $"transfer-sites-api-{Guid.NewGuid():N}"),
          ["TransferCs:Sites:alpha:Hosts:0"] = "alpha.test",
          ["TransferCs:Sites:alpha:Title"] = "Alpha files",
          ["TransferCs:Sites:alpha:BaseUrl"] = "https://cdn.alpha.test",
          ["TransferCs:Sites:beta:Hosts:0"] = "beta.test",
          ["TransferCs:Sites:beta:Title"] = "Beta files"
        })));

  private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
    string host, string? content = null, string? token = null)
  {
    using HttpRequestMessage request = new(method, path);
    request.Headers.Host = host;
    if (token != null)
      request.Headers.Add("Token", token);
    if (content != null)
    {
      request.Content = new StringContent(content);
      request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
    }
    return await client.SendAsync(request);
  }
}
