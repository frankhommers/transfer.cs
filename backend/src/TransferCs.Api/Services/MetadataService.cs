using System.Collections.Concurrent;
using System.Text.Json;
using TransferCs.Api.Models;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Services;

public class MetadataService
{
    private readonly IStorageProvider _storage;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public MetadataService(IStorageProvider storage)
    {
        _storage = storage;
    }

    public async Task SaveAsync(string token, string filename, FileMetadata metadata, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(metadata);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await _storage.PutAsync(token, $"{filename}.metadata", stream, "application/json", (ulong)stream.Length, ct);
    }

    public async Task<FileMetadata?> LoadAsync(string token, string filename, CancellationToken ct = default)
    {
        try
        {
            var (content, _) = await _storage.GetAsync(token, $"{filename}.metadata", null, ct);
            await using (content)
            {
                return await JsonSerializer.DeserializeAsync<FileMetadata>(content, cancellationToken: ct);
            }
        }
        catch (Exception ex) when (_storage.IsNotExist(ex))
        {
            return null;
        }
    }

    public async Task<FileMetadata?> CheckAndLoadAsync(string token, string filename,
        bool incrementDownload = false, CancellationToken ct = default)
    {
        var key = $"{token}/{filename}";
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            var metadata = await LoadAsync(token, filename, ct);
            if (metadata == null) return null;

            if (metadata.IsMaxDownloadsExpired || metadata.IsMaxDateExpired)
                return null;

            if (incrementDownload)
            {
                metadata.Downloads++;
                await SaveAsync(token, filename, metadata, ct);
            }

            return metadata;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task IncrementDownloadsAsync(string token, string filename, CancellationToken ct = default)
    {
        var key = $"{token}/{filename}";
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            var metadata = await LoadAsync(token, filename, ct);
            if (metadata == null) return;

            metadata.Downloads++;
            await SaveAsync(token, filename, metadata, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<bool> ValidateDeletionTokenAsync(string token, string filename,
        string deletionToken, CancellationToken ct = default)
    {
        var metadata = await LoadAsync(token, filename, ct);
        if (metadata == null) return false;
        return metadata.DeletionToken == deletionToken;
    }
}
