using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TransferCs.Api.Tests.Endpoints;

public class DeleteEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public DeleteEndpointsTests(WebApplicationFactory<Program> factory)
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
  public async Task Delete_WithValidToken_Succeeds()
  {
    byte[] originalContent = "Delete me!"u8.ToArray();
    (string url, string deleteUrl) = await UploadFile("deletable.txt", originalContent);

    // Delete the file using the deletion URL
    Uri deleteUri = new(deleteUrl);
    HttpResponseMessage deleteResponse = await _client.DeleteAsync(deleteUri.PathAndQuery);

    Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

    // Verify the file is no longer accessible
    Uri getUri = new(url);
    HttpResponseMessage getResponse = await _client.GetAsync(getUri.PathAndQuery);
    Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
  }

  [Fact]
  public async Task Delete_WithInvalidToken_Returns404()
  {
    byte[] originalContent = "Can't delete me with wrong token!"u8.ToArray();
    (string url, _) = await UploadFile("protected.txt", originalContent);

    // Try to delete with invalid token
    Uri uri = new(url);
    HttpResponseMessage deleteResponse = await _client.DeleteAsync($"{uri.PathAndQuery}/invalidtoken");

    Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
  }
}