using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TransferCs.Api.Tests.Endpoints;

public class UploadEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public UploadEndpointsTests(WebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task Put_UploadsFile_ReturnsUrl()
  {
    ByteArrayContent content = new("Hello transfer.sh!"u8.ToArray());
    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

    HttpResponseMessage response = await _client.PutAsync("/put/test.txt", content);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    string body = await response.Content.ReadAsStringAsync();
    Assert.Contains("/test.txt", body);

    // Should have X-Url-Delete header
    Assert.True(response.Headers.Contains("X-Url-Delete"));
  }

  [Fact]
  public async Task Put_EmptyContent_Returns400()
  {
    ByteArrayContent content = new(Array.Empty<byte>());
    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    content.Headers.ContentLength = 0;

    HttpResponseMessage response = await _client.PutAsync("/put/empty.txt", content);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Post_MultipartUpload_ReturnsUrl()
  {
    MultipartFormDataContent multipartContent = new();
    ByteArrayContent fileContent = new("File content here"u8.ToArray());
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
    multipartContent.Add(fileContent, "file", "upload.txt");

    HttpResponseMessage response = await _client.PostAsync("/", multipartContent);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    string body = await response.Content.ReadAsStringAsync();
    Assert.Contains("/upload.txt", body);
  }

  [Fact]
  public async Task ConcurrentPut_WithSameCustomToken_AllowsOnlyOneUpload()
  {
    string token = $"concurrent-{Guid.NewGuid():N}";
    HttpRequestMessage first = CreatePutRequest(token, "first");
    HttpRequestMessage second = CreatePutRequest(token, "second");

    HttpResponseMessage[] responses = await Task.WhenAll(
      _client.SendAsync(first),
      _client.SendAsync(second));

    Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
    Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    foreach (HttpResponseMessage response in responses)
      response.Dispose();
  }

  [Fact]
  public async Task Multipart_WithCustomTokenAndMultipleFiles_IsRejectedBeforeUpload()
  {
    string token = $"multi-{Guid.NewGuid():N}";
    using MultipartFormDataContent content = new();
    content.Add(new StringContent("first"), "file", "first.txt");
    content.Add(new StringContent("second"), "file", "second.txt");
    using HttpRequestMessage request = new(HttpMethod.Post, "/") { Content = content };
    request.Headers.Add("Token", token);

    using HttpResponseMessage response = await _client.SendAsync(request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    using HttpResponseMessage download = await _client.GetAsync($"/{token}/first.txt");
    Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
  }

  private static HttpRequestMessage CreatePutRequest(string token, string content)
  {
    HttpRequestMessage request = new(HttpMethod.Put, "/put/concurrent.txt")
    {
      Content = new StringContent(content)
    };
    request.Headers.Add("Token", token);
    return request;
  }
}
