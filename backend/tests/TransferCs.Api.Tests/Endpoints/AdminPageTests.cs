using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TransferCs.Api.Tests.Endpoints;

public class AdminPageTests : IAsyncLifetime
{
  private const string Page = "<!doctype html><html><body><div id=\"root\"></div></body></html>";
  private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"transfer-admin-page-{Guid.NewGuid():N}");
  private WebApplicationFactory<Program> _factory = null!;

  public async Task InitializeAsync()
  {
    Directory.CreateDirectory(_webRoot);
    await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), Page);
    _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot));
  }

  public async Task DisposeAsync()
  {
    await _factory.DisposeAsync();
    Directory.Delete(_webRoot, true);
  }

  [Theory]
  [InlineData("file.txt")]
  [InlineData("files.zip")]
  [InlineData("no-extension")]
  [InlineData("rapport%20%232.txt")]
  public async Task AdminLink_ServesApplicationWithoutExposingFileMetadataAsync(string filename)
  {
    using HttpClient client = _factory.CreateClient();
    using HttpRequestMessage request = new(HttpMethod.Get, $"/admin/missing-token/{filename}");
    request.Headers.Accept.ParseAdd("text/html");
    using HttpResponseMessage response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
    Assert.Equal(Page, await response.Content.ReadAsStringAsync());
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());

    using HttpResponseMessage metadata = await client.GetAsync($"/api/admin/missing-token/{filename}");
    Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
    Assert.Empty(await metadata.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task AdminLink_HeadReturnsHtmlHeadersWithoutBodyAsync()
  {
    using HttpClient client = _factory.CreateClient();
    using HttpRequestMessage request = new(HttpMethod.Head, "/admin/missing-token/files.zip");
    using HttpResponseMessage response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
    Assert.Equal(Page.Length, response.Content.Headers.ContentLength);
    Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
  }

  [Theory]
  [InlineData("/unknown/missing-token/file.txt")]
  [InlineData("/download/missing-token/file.txt")]
  public async Task NonAdminRoutes_KeepTheirNotFoundResponseAsync(string path)
  {
    using HttpClient client = _factory.CreateClient();
    using HttpResponseMessage response = await client.GetAsync(path);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Empty(await response.Content.ReadAsStringAsync());
  }
}
