using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Tests.Services;

public class PurgeBackgroundServiceTests : IDisposable
{
  private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"transfer-purge-{Guid.NewGuid():N}");

  [Fact]
  public async Task StartAsync_PurgesBeforeFirstInterval()
  {
    TransferCsOptions options = new()
    {
      BasePath = _basePath,
      PurgeDays = 1,
      PurgeIntervalHours = 24
    };
    SiteResolver resolver = new(Options.Create(options));
    SiteStorageFactory factory = new(Options.Create(options), resolver);
    IStorageProvider storage = factory.Get(resolver.LegacySite);
    using MemoryStream content = new("expired"u8.ToArray());
    await storage.PutAsync("expired-token", "file.txt", content, "text/plain", 7);
    string filePath = Path.Combine(_basePath, "expired-token", "file.txt");
    File.SetCreationTimeUtc(filePath, DateTime.UtcNow.AddDays(-2));
    PurgeBackgroundService service = new(
      factory,
      resolver,
      Options.Create(options),
      NullLogger<PurgeBackgroundService>.Instance);

    await service.StartAsync(CancellationToken.None);
    DateTime deadline = DateTime.UtcNow.AddSeconds(1);
    while (File.Exists(filePath) && DateTime.UtcNow < deadline)
      await Task.Delay(10);
    await service.StopAsync(CancellationToken.None);

    Assert.False(File.Exists(filePath));
  }

  public void Dispose()
  {
    if (Directory.Exists(_basePath))
      Directory.Delete(_basePath, true);
  }
}
