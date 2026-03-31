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
        var content = new ByteArrayContent("Hello transfer.sh!"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _client.PutAsync("/put/test.txt", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("/test.txt", body);

        // Should have X-Url-Delete header
        Assert.True(response.Headers.Contains("X-Url-Delete"));
    }

    [Fact]
    public async Task Put_EmptyContent_Returns400()
    {
        var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = 0;

        var response = await _client.PutAsync("/put/empty.txt", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_MultipartUpload_ReturnsUrl()
    {
        var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("File content here"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        multipartContent.Add(fileContent, "file", "upload.txt");

        var response = await _client.PostAsync("/", multipartContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("/upload.txt", body);
    }
}
