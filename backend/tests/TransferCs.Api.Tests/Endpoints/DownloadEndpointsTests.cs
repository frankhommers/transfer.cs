using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TransferCs.Api.Tests.Endpoints;

public class DownloadEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public DownloadEndpointsTests(WebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  private async Task<(string Url, string DeleteUrl)> UploadFile(string filename, byte[] content)
  {
    ByteArrayContent httpContent = new(content);
    httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

    HttpResponseMessage response = await _client.PutAsync($"/put/{filename}", httpContent);
    response.EnsureSuccessStatusCode();

    string url = (await response.Content.ReadAsStringAsync()).Trim();
    string deleteUrl = response.Headers.GetValues("X-Url-Delete").First();

    return (url, deleteUrl);
  }

  [Fact]
  public async Task Get_DownloadsFile()
  {
    byte[] originalContent = "Hello download test!"u8.ToArray();
    (string url, _) = await UploadFile("download.txt", originalContent);

    // url is full URL like http://localhost/token/download.txt
    // Extract the path part
    Uri uri = new(url);
    HttpResponseMessage response = await _client.GetAsync(uri.PathAndQuery);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    byte[] body = await response.Content.ReadAsByteArrayAsync();
    Assert.Equal(originalContent, body);
  }

  [Fact]
  public async Task Head_ReturnsHeaders()
  {
    byte[] originalContent = "Head test content"u8.ToArray();
    (string url, _) = await UploadFile("head.txt", originalContent);

    Uri uri = new(url);
    HttpRequestMessage request = new(HttpMethod.Head, uri.PathAndQuery);
    HttpResponseMessage response = await _client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_WithRange_ReturnsPartialContent()
  {
    byte[] originalContent = "0123456789ABCDEF"u8.ToArray();
    (string url, _) = await UploadFile("range.txt", originalContent);

    Uri uri = new(url);
    HttpRequestMessage request = new(HttpMethod.Get, uri.PathAndQuery);
    request.Headers.Range = new RangeHeaderValue(5, 9);

    HttpResponseMessage response = await _client.SendAsync(request);

    Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
    byte[] body = await response.Content.ReadAsByteArrayAsync();
    Assert.Equal("56789"u8.ToArray(), body);
  }

  [Fact]
  public async Task Get_NonExistent_Returns404()
  {
    HttpResponseMessage response = await _client.GetAsync("/nonexistenttoken/nofile.txt");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }
}