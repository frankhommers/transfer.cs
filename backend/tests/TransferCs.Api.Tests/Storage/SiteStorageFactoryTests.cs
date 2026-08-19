using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Tests.Storage;

public class SiteStorageFactoryTests : IDisposable
{
  private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"transfer-sites-{Guid.NewGuid():N}");

  [Fact]
  public async Task Get_StoresIdenticalKeysInDifferentSiteDirectories()
  {
    TransferCsOptions options = CreateOptions();
    SiteResolver resolver = new(Options.Create(options));
    SiteStorageFactory factory = new(Options.Create(options), resolver);
    ResolvedSite alpha = resolver.Resolve("alpha.test")!;
    ResolvedSite beta = resolver.Resolve("beta.test")!;

    using MemoryStream alphaContent = new("alpha"u8.ToArray());
    using MemoryStream betaContent = new("beta"u8.ToArray());
    await factory.Get(alpha).PutAsync("same-token", "file.txt", alphaContent, "text/plain", 5);
    await factory.Get(beta).PutAsync("same-token", "file.txt", betaContent, "text/plain", 4);

    Assert.Equal("alpha", await ReadAsync(factory.Get(alpha), "same-token", "file.txt"));
    Assert.Equal("beta", await ReadAsync(factory.Get(beta), "same-token", "file.txt"));
  }

  public void Dispose()
  {
    if (Directory.Exists(_basePath))
      Directory.Delete(_basePath, true);
  }

  private TransferCsOptions CreateOptions() => new()
  {
    BasePath = _basePath,
    InitialSiteId = "alpha",
    Sites = new Dictionary<string, SiteOptions>
    {
      ["alpha"] = new() { Hosts = ["alpha.test"] },
      ["beta"] = new() { Hosts = ["beta.test"] }
    }
  };

  private static async Task<string> ReadAsync(IStorageProvider storage, string token, string filename)
  {
    (Stream content, _) = await storage.GetAsync(token, filename, null);
    await using (content)
    using (StreamReader reader = new(content))
      return await reader.ReadToEndAsync();
  }
}
