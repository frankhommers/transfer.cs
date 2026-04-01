namespace TransferCs.Api.Storage;

public interface IStorageProvider
{
  Task<(Stream Content, ulong ContentLength)> GetAsync(
    string token, string filename, StorageRange? range, CancellationToken ct = default);

  Task<ulong> HeadAsync(string token, string filename, CancellationToken ct = default);

  Task PutAsync(string token, string filename, Stream content,
    string contentType, ulong contentLength, CancellationToken ct = default);

  Task<bool> ExistsAsync(string token, CancellationToken ct = default);
  Task DeleteAsync(string token, string filename, CancellationToken ct = default);
  Task PurgeAsync(TimeSpan maxAge, CancellationToken ct = default);
  bool IsNotExist(Exception ex);
  bool IsRangeSupported { get; }
  string Type { get; }
}