using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TransferCs.Api.Tests.Services;

public class MigrationStartupTests : IDisposable
{
  private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"transfer-startup-{Guid.NewGuid():N}");

  [Fact]
  public async Task NormalStartup_DoesNotMoveLegacyData()
  {
    string legacyDirectory = Path.Combine(_basePath, "legacy-token");
    Directory.CreateDirectory(legacyDirectory);
    File.WriteAllText(Path.Combine(legacyDirectory, "file.txt"), "legacy");
    await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
      .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["TransferCs:BasePath"] = _basePath,
          ["TransferCs:InitialSiteId"] = "alpha",
          ["TransferCs:Sites:alpha:Hosts:0"] = "alpha.test"
        })));
    using HttpClient client = factory.CreateClient();

    using HttpResponseMessage response = await client.GetAsync("/health");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(File.Exists(Path.Combine(legacyDirectory, "file.txt")));
    Assert.False(Directory.Exists(Path.Combine(_basePath, "alpha", "legacy-token")));
  }

  public void Dispose()
  {
    if (Directory.Exists(_basePath))
      Directory.Delete(_basePath, true);
  }
}
