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
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task PutAndGet_RoundTrips()
    {
        var content = "Hello, transfer.sh!"u8.ToArray();
        using var putStream = new MemoryStream(content);
        await _provider.PutAsync("token1", "test.txt", putStream, "text/plain", (ulong)content.Length);

        var (getStream, contentLength) = await _provider.GetAsync("token1", "test.txt", null);
        await using (getStream)
        {
            using var ms = new MemoryStream();
            await getStream.CopyToAsync(ms);
            Assert.Equal(content, ms.ToArray());
            Assert.Equal((ulong)content.Length, contentLength);
        }
    }

    [Fact]
    public async Task Head_ReturnsContentLength()
    {
        var content = "Test content for head"u8.ToArray();
        using var putStream = new MemoryStream(content);
        await _provider.PutAsync("token2", "head.txt", putStream, "text/plain", (ulong)content.Length);

        var length = await _provider.HeadAsync("token2", "head.txt");
        Assert.Equal((ulong)content.Length, length);
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var content = "Delete me"u8.ToArray();
        using var putStream = new MemoryStream(content);
        await _provider.PutAsync("token3", "delete.txt", putStream, "text/plain", (ulong)content.Length);

        await _provider.DeleteAsync("token3", "delete.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _provider.GetAsync("token3", "delete.txt", null));
    }

    [Fact]
    public async Task Get_NonExistent_Throws()
    {
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _provider.GetAsync("notoken", "nofile.txt", null));
        Assert.True(_provider.IsNotExist(ex));
    }

    [Fact]
    public async Task Get_WithRange_ReturnsPartialContent()
    {
        var content = "0123456789ABCDEF"u8.ToArray();
        using var putStream = new MemoryStream(content);
        await _provider.PutAsync("token4", "range.txt", putStream, "text/plain", (ulong)content.Length);

        var range = new StorageRange { Start = 5, Limit = 5 };
        var (getStream, contentLength) = await _provider.GetAsync("token4", "range.txt", range);
        await using (getStream)
        {
            using var ms = new MemoryStream();
            await getStream.CopyToAsync(ms);
            var result = ms.ToArray();
            Assert.Equal(5, result.Length);
            Assert.Equal("56789"u8.ToArray(), result);
            Assert.Equal(5UL, contentLength);
        }
    }

    [Fact]
    public async Task Purge_RemovesOldFiles()
    {
        var content = "Purge me"u8.ToArray();
        using var putStream = new MemoryStream(content);
        await _provider.PutAsync("token5", "purge.txt", putStream, "text/plain", (ulong)content.Length);

        // Set the file creation time to the past
        var filePath = Path.Combine(_tempDir, "token5", "purge.txt");
        File.SetCreationTimeUtc(filePath, DateTime.UtcNow.AddDays(-10));

        await _provider.PurgeAsync(TimeSpan.FromDays(1));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _provider.GetAsync("token5", "purge.txt", null));
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
}
