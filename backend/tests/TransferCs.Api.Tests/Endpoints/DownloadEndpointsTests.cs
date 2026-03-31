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
        var httpContent = new ByteArrayContent(content);
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _client.PutAsync($"/put/{filename}", httpContent);
        response.EnsureSuccessStatusCode();

        var url = (await response.Content.ReadAsStringAsync()).Trim();
        var deleteUrl = response.Headers.GetValues("X-Url-Delete").First();

        return (url, deleteUrl);
    }

    [Fact]
    public async Task Get_DownloadsFile()
    {
        var originalContent = "Hello download test!"u8.ToArray();
        var (url, _) = await UploadFile("download.txt", originalContent);

        // url is full URL like http://localhost/token/download.txt
        // Extract the path part
        var uri = new Uri(url);
        var response = await _client.GetAsync(uri.PathAndQuery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(originalContent, body);
    }

    [Fact]
    public async Task Head_ReturnsHeaders()
    {
        var originalContent = "Head test content"u8.ToArray();
        var (url, _) = await UploadFile("head.txt", originalContent);

        var uri = new Uri(url);
        var request = new HttpRequestMessage(HttpMethod.Head, uri.PathAndQuery);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithRange_ReturnsPartialContent()
    {
        var originalContent = "0123456789ABCDEF"u8.ToArray();
        var (url, _) = await UploadFile("range.txt", originalContent);

        var uri = new Uri(url);
        var request = new HttpRequestMessage(HttpMethod.Get, uri.PathAndQuery);
        request.Headers.Range = new RangeHeaderValue(5, 9);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("56789"u8.ToArray(), body);
    }

    [Fact]
    public async Task Get_NonExistent_Returns404()
    {
        var response = await _client.GetAsync("/nonexistenttoken/nofile.txt");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
