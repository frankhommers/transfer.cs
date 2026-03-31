using TransferCs.Api.Models;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Tests.Services;

public class MetadataServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalStorageProvider _storage;
    private readonly MetadataService _service;

    public MetadataServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"transfersh-meta-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalStorageProvider(_tempDir);
        _service = new MetadataService(_storage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        // First create the token directory by putting a dummy file
        using var dummyStream = new MemoryStream("dummy"u8.ToArray());
        await _storage.PutAsync("token1", "file.txt", dummyStream, "text/plain", 5);

        var metadata = new FileMetadata
        {
            ContentType = "text/plain",
            ContentLength = 1024,
            Downloads = 5,
            MaxDownloads = 10,
            DeletionToken = "del123",
            Encrypted = false,
        };

        await _service.SaveAsync("token1", "file.txt", metadata);
        var loaded = await _service.LoadAsync("token1", "file.txt");

        Assert.NotNull(loaded);
        Assert.Equal("text/plain", loaded.ContentType);
        Assert.Equal(1024, loaded.ContentLength);
        Assert.Equal(5, loaded.Downloads);
        Assert.Equal(10, loaded.MaxDownloads);
        Assert.Equal("del123", loaded.DeletionToken);
        Assert.False(loaded.Encrypted);
    }

    [Fact]
    public async Task IncrementDownloads_Updates()
    {
        // First create the token directory by putting a dummy file
        using var dummyStream = new MemoryStream("dummy"u8.ToArray());
        await _storage.PutAsync("token2", "file.txt", dummyStream, "text/plain", 5);

        var metadata = new FileMetadata
        {
            ContentType = "application/octet-stream",
            ContentLength = 2048,
            Downloads = 0,
            MaxDownloads = 100,
        };

        await _service.SaveAsync("token2", "file.txt", metadata);
        await _service.IncrementDownloadsAsync("token2", "file.txt");

        var loaded = await _service.LoadAsync("token2", "file.txt");
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.Downloads);
    }
}
