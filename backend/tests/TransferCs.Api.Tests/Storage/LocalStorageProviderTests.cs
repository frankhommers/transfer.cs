using TransferCs.Api.Storage;

namespace TransferCs.Api.Tests.Storage;

public class LocalStorageProviderTests : IDisposable
{
  private readonly string _tempDir;
  private readonly LocalStorageProvider _provider;

  public LocalStorageProviderTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"transfersh-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
    _provider = new LocalStorageProvider(_tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, true);
  }

  [Fact]
  public async Task PutAndGet_RoundTrips()
  {
    byte[] content = "Hello, transfer.sh!"u8.ToArray();
    using MemoryStream putStream = new(content);
    await _provider.PutAsync("token1", "test.txt", putStream, "text/plain", (ulong)content.Length);

    (Stream getStream, ulong contentLength) = await _provider.GetAsync("token1", "test.txt", null);
    await using (getStream)
    {
      using MemoryStream ms = new();
      await getStream.CopyToAsync(ms);
      Assert.Equal(content, ms.ToArray());
      Assert.Equal((ulong)content.Length, contentLength);
    }
  }

  [Fact]
  public async Task FailedOverwrite_PreservesExistingFile()
  {
    byte[] original = "original"u8.ToArray();
    using MemoryStream originalStream = new(original);
    await _provider.PutAsync("atomic", "file.txt", originalStream, "text/plain", (ulong)original.Length);
    await using FailingCopyStream failingStream = new();

    await Assert.ThrowsAsync<IOException>(() =>
      _provider.PutAsync("atomic", "file.txt", failingStream, "text/plain", 4));

    (Stream content, _) = await _provider.GetAsync("atomic", "file.txt", null);
    await using (content)
    {
      using MemoryStream result = new();
      await content.CopyToAsync(result);
      Assert.Equal(original, result.ToArray());
    }
  }

  [Fact]
  public async Task TryReserveToken_AllowsOnlyOneConcurrentOwner()
  {
    Task<bool>[] reservations = Enumerable.Range(0, 10)
      .Select(_ => _provider.TryReserveTokenAsync("reserved"))
      .ToArray();

    bool[] results = await Task.WhenAll(reservations);

    Assert.Single(results, result => result);
    await _provider.ReleaseTokenAsync("reserved");
  }

  [Fact]
  public async Task TryReserveToken_ReclaimsStaleReservation()
  {
    Assert.True(await _provider.TryReserveTokenAsync("stale"));
    string reservationPath = Path.Combine(_tempDir, "stale", ".upload-reservation");
    File.SetLastWriteTimeUtc(reservationPath, DateTime.UtcNow.AddDays(-2));

    bool reserved = await _provider.TryReserveTokenAsync("stale");

    Assert.True(reserved);
    await _provider.ReleaseTokenAsync("stale");
  }

  [Fact]
  public async Task Head_ReturnsContentLength()
  {
    byte[] content = "Test content for head"u8.ToArray();
    using MemoryStream putStream = new(content);
    await _provider.PutAsync("token2", "head.txt", putStream, "text/plain", (ulong)content.Length);

    ulong length = await _provider.HeadAsync("token2", "head.txt");
    Assert.Equal((ulong)content.Length, length);
  }

  [Fact]
  public async Task Delete_RemovesFile()
  {
    byte[] content = "Delete me"u8.ToArray();
    using MemoryStream putStream = new(content);
    await _provider.PutAsync("token3", "delete.txt", putStream, "text/plain", (ulong)content.Length);

    await _provider.DeleteAsync("token3", "delete.txt");

    await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.GetAsync("token3", "delete.txt", null));
  }

  [Fact]
  public async Task Get_NonExistent_Throws()
  {
    FileNotFoundException ex =
      await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.GetAsync("notoken", "nofile.txt", null));
    Assert.True(_provider.IsNotExist(ex));
  }

  [Theory]
  [InlineData("..", "file.txt")]
  [InlineData("token", "../file.txt")]
  [InlineData("token", "folder/file.txt")]
  [InlineData("token", "folder\\file.txt")]
  public async Task Get_PathTraversal_Throws(string token, string filename)
  {
    await Assert.ThrowsAsync<ArgumentException>(() => _provider.GetAsync(token, filename, null));
  }

  [Fact]
  public async Task Get_WithRange_ReturnsPartialContent()
  {
    byte[] content = "0123456789ABCDEF"u8.ToArray();
    using MemoryStream putStream = new(content);
    await _provider.PutAsync("token4", "range.txt", putStream, "text/plain", (ulong)content.Length);

    StorageRange range = new() { Start = 5, Limit = 5 };
    (Stream getStream, ulong contentLength) = await _provider.GetAsync("token4", "range.txt", range);
    await using (getStream)
    {
      using MemoryStream ms = new();
      await getStream.CopyToAsync(ms);
      byte[] result = ms.ToArray();
      Assert.Equal(5, result.Length);
      Assert.Equal("56789"u8.ToArray(), result);
      Assert.Equal(5UL, contentLength);
    }
  }

  [Fact]
  public async Task Purge_RemovesOldFiles()
  {
    byte[] content = "Purge me"u8.ToArray();
    using MemoryStream putStream = new(content);
    await _provider.PutAsync("token5", "purge.txt", putStream, "text/plain", (ulong)content.Length);

    // Set the file creation time to the past
    string filePath = Path.Combine(_tempDir, "token5", "purge.txt");
    File.SetCreationTimeUtc(filePath, DateTime.UtcNow.AddDays(-10));

    await _provider.PurgeAsync(TimeSpan.FromDays(1));

    await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.GetAsync("token5", "purge.txt", null));
  }

  [Fact]
  public async Task Purge_RemovesMetadataTogetherWithOldPayload()
  {
    using MemoryStream payload = new("payload"u8.ToArray());
    using MemoryStream metadata = new("metadata"u8.ToArray());
    await _provider.PutAsync("token6", "file.txt", payload, "text/plain", 7);
    await _provider.PutAsync("token6", "file.txt.metadata", metadata, "application/json", 8);
    string payloadPath = Path.Combine(_tempDir, "token6", "file.txt");
    File.SetCreationTimeUtc(payloadPath, DateTime.UtcNow.AddDays(-10));

    await _provider.PurgeAsync(TimeSpan.FromDays(1));

    await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.GetAsync("token6", "file.txt", null));
    await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.GetAsync("token6", "file.txt.metadata", null));
  }

  [Fact]
  public async Task Purge_PreservesLegitimateMetadataExtensionPayload()
  {
    using MemoryStream payload = new("payload"u8.ToArray());
    using MemoryStream metadata = new("metadata"u8.ToArray());
    await _provider.PutAsync("token7", "report.metadata", payload, "text/plain", 7);
    await _provider.PutAsync("token7", "report.metadata.metadata", metadata, "application/json", 8);

    await _provider.PurgeAsync(TimeSpan.FromDays(1));

    Assert.Equal(7UL, await _provider.HeadAsync("token7", "report.metadata"));
    Assert.Equal(8UL, await _provider.HeadAsync("token7", "report.metadata.metadata"));
  }

  [Fact]
  public async Task Purge_SkipsActivelyReservedToken()
  {
    Assert.True(await _provider.TryReserveTokenAsync("token8"));
    using MemoryStream payload = new("payload"u8.ToArray());
    await _provider.PutAsync("token8", "file.txt", payload, "text/plain", 7);
    string payloadPath = Path.Combine(_tempDir, "token8", "file.txt");
    File.SetCreationTimeUtc(payloadPath, DateTime.UtcNow.AddDays(-10));

    await _provider.PurgeAsync(TimeSpan.FromDays(1));

    Assert.Equal(7UL, await _provider.HeadAsync("token8", "file.txt"));
    await _provider.ReleaseTokenAsync("token8");
  }

  [Fact]
  public void Type_ReturnsLocal()
  {
    Assert.Equal("local", _provider.Type);
  }

  [Fact]
  public void IsRangeSupported_ReturnsTrue()
  {
    Assert.True(_provider.IsRangeSupported);
  }

  private sealed class FailingCopyStream : MemoryStream
  {
    public override async Task CopyToAsync(Stream destination, int bufferSize,
      CancellationToken cancellationToken)
    {
      await destination.WriteAsync("new!"u8.ToArray(), cancellationToken);
      throw new IOException("Simulated interrupted write");
    }
  }
}
