using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TransferCs.Api.Models;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Services;

public class MetadataService
{
  private readonly IStorageProvider _storage;
  private readonly KeyedLock _locks;
  private readonly string _siteId;

  public MetadataService(IStorageProvider storage)
    : this(storage, new KeyedLock(), "default")
  {
  }

  public MetadataService(IStorageProvider storage, SiteContext siteContext, KeyedLock locks)
    : this(storage, locks, siteContext.Site.Id)
  {
  }

  private MetadataService(IStorageProvider storage, KeyedLock locks, string siteId)
  {
    _storage = storage;
    _locks = locks;
    _siteId = siteId;
  }

  public async Task SaveAsync(string token, string filename, FileMetadata metadata, CancellationToken ct = default)
  {
    string json = JsonSerializer.Serialize(metadata, AppJsonContext.Default.FileMetadata);
    using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(json));
    await _storage.PutAsync(token, $"{filename}.metadata", stream, "application/json", (ulong)stream.Length, ct);
  }

  public async Task<FileMetadata?> LoadAsync(string token, string filename, CancellationToken ct = default)
  {
    try
    {
      (Stream content, _) = await _storage.GetAsync(token, $"{filename}.metadata", null, ct);
      await using (content)
      {
        return await JsonSerializer.DeserializeAsync(content, AppJsonContext.Default.FileMetadata, ct);
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
    return await CheckAndLoadCoreAsync(token, filename, incrementDownload, null, 0, ct);
  }

  public async Task<FileMetadata?> CheckAndRecordDownloadAsync(string token, string filename,
    string? downloadIp, int maxLogEntries, string expectedGeneration, CancellationToken ct = default)
  {
    return await CheckAndLoadCoreAsync(token, filename, true, downloadIp, maxLogEntries, ct, expectedGeneration);
  }

  private async Task<FileMetadata?> CheckAndLoadCoreAsync(string token, string filename,
    bool incrementDownload, string? downloadIp, int maxLogEntries, CancellationToken ct,
    string? expectedGeneration = null)
  {
    SemaphoreSlim semaphore = GetLock(token, filename);
    await semaphore.WaitAsync(ct);
    try
    {
      FileMetadata? metadata = await LoadAsync(token, filename, ct);
      if (metadata == null) return null;
      if (expectedGeneration != null &&
          !string.Equals(metadata.Generation, expectedGeneration, StringComparison.Ordinal))
        return null;

      if (metadata.IsMaxDownloadsExpired || metadata.IsMaxDateExpired)
        return null;

      if (incrementDownload)
      {
        metadata.Downloads++;

        if (!string.IsNullOrEmpty(downloadIp))
        {
          metadata.DownloadLogTotal++;
          if (maxLogEntries > 0)
          {
            metadata.DownloadLog.Add(new DownloadEntry(downloadIp, DateTime.UtcNow));
            if (metadata.DownloadLog.Count > maxLogEntries)
              metadata.DownloadLog.RemoveRange(0, metadata.DownloadLog.Count - maxLogEntries);
          }
        }

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
    SemaphoreSlim semaphore = GetLock(token, filename);
    await semaphore.WaitAsync(ct);
    try
    {
      FileMetadata? metadata = await LoadAsync(token, filename, ct);
      if (metadata == null) return;

      metadata.Downloads++;
      await SaveAsync(token, filename, metadata, ct);
    }
    finally
    {
      semaphore.Release();
    }
  }

  public async Task<FileMetadata?> LoadForAdminAsync(string token, string filename,
    string adminToken, CancellationToken ct = default)
  {
    SemaphoreSlim semaphore = GetLock(token, filename);
    await semaphore.WaitAsync(ct);
    try
    {
      FileMetadata? metadata = await LoadAsync(token, filename, ct);
      if (metadata == null || string.IsNullOrEmpty(metadata.AdminToken) ||
          string.IsNullOrEmpty(adminToken) || !FixedTimeEquals(metadata.AdminToken, adminToken))
        return null;

      return metadata;
    }
    finally
    {
      semaphore.Release();
    }
  }

  public async Task<bool> ValidateAdminTokenAsync(string token, string filename,
    string adminToken, CancellationToken ct = default)
  {
    return await LoadForAdminAsync(token, filename, adminToken, ct) != null;
  }

  public async Task<bool> ValidateDeletionTokenAsync(string token, string filename,
    string deletionToken, CancellationToken ct = default)
  {
    FileMetadata? metadata = await LoadAsync(token, filename, ct);
    return metadata != null && !string.IsNullOrEmpty(metadata.DeletionToken) &&
           !string.IsNullOrEmpty(deletionToken) &&
           FixedTimeEquals(metadata.DeletionToken, deletionToken);
  }

  public Task<bool> DeleteForAdminAsync(string token, string filename, string adminToken,
    CancellationToken ct = default) =>
    DeleteAsync(token, filename, adminToken, true, ct);

  public Task<bool> DeleteWithDeletionTokenAsync(string token, string filename, string deletionToken,
    CancellationToken ct = default) =>
    DeleteAsync(token, filename, deletionToken, false, ct);

  private async Task<bool> DeleteAsync(string token, string filename, string suppliedToken,
    bool useAdminToken, CancellationToken ct)
  {
    SemaphoreSlim semaphore = GetLock(token, filename);
    await semaphore.WaitAsync(ct);
    try
    {
      FileMetadata? metadata = await LoadAsync(token, filename, ct);
      string expectedToken = useAdminToken ? metadata?.AdminToken ?? "" : metadata?.DeletionToken ?? "";
      if (string.IsNullOrEmpty(expectedToken) || string.IsNullOrEmpty(suppliedToken) ||
          !FixedTimeEquals(expectedToken, suppliedToken))
        return false;

      await _storage.DeleteAsync(token, filename, ct);
      await _storage.DeleteAsync(token, $"{filename}.metadata", ct);
      return true;
    }
    catch (Exception ex) when (_storage.IsNotExist(ex))
    {
      return false;
    }
    finally
    {
      semaphore.Release();
    }
  }

  private SemaphoreSlim GetLock(string token, string filename)
    => _locks.Get(_siteId, token, filename);

  private static bool FixedTimeEquals(string expected, string actual)
  {
    byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
    byte[] actualBytes = Encoding.UTF8.GetBytes(actual);
    return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
  }
}
