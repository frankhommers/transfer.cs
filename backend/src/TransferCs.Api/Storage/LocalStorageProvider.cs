namespace TransferCs.Api.Storage;

public class LocalStorageProvider : IStorageProvider
{
  private static readonly TimeSpan ReservationLifetime = TimeSpan.FromDays(1);
  private readonly string _basePath;

  public LocalStorageProvider(string basePath)
  {
    _basePath = basePath;
  }

  public bool IsRangeSupported => true;
  public string Type => "local";

  public async Task PutAsync(string token, string filename, Stream content,
    string contentType, ulong contentLength, CancellationToken ct = default)
  {
    StoragePath.EnsureSafeSegment(token, nameof(token));
    StoragePath.EnsureSafeSegment(filename, nameof(filename));
    string dir = Path.Combine(_basePath, token);
    Directory.CreateDirectory(dir);

    string filePath = Path.Combine(dir, filename);
    string tempPath = Path.Combine(dir, $".{filename}.{Guid.NewGuid():N}.tmp");
    try
    {
      await using (FileStream fs = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
      {
        await content.CopyToAsync(fs, ct);
        await fs.FlushAsync(ct);
      }

      File.Move(tempPath, filePath, true);
    }
    finally
    {
      File.Delete(tempPath);
    }
  }

  public Task<(Stream Content, ulong ContentLength)> GetAsync(
    string token, string filename, StorageRange? range, CancellationToken ct = default)
  {
    StoragePath.EnsureSafeSegment(token, nameof(token));
    StoragePath.EnsureSafeSegment(filename, nameof(filename));
    string filePath = Path.Combine(_basePath, token, filename);
    FileInfo fi = new(filePath);
    if (!fi.Exists)
      throw new FileNotFoundException($"File not found: {filePath}", filePath);

    ulong contentLength = (ulong)fi.Length;
    Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

    if (range != null)
    {
      ulong acceptedLength = range.AcceptLength(contentLength);
      stream.Seek((long)range.Start, SeekOrigin.Begin);
      stream = new LimitedStream(stream, (long)acceptedLength);
      contentLength = acceptedLength;
    }

    return Task.FromResult((stream, contentLength));
  }

  public Task<ulong> HeadAsync(string token, string filename, CancellationToken ct = default)
  {
    StoragePath.EnsureSafeSegment(token, nameof(token));
    StoragePath.EnsureSafeSegment(filename, nameof(filename));
    string filePath = Path.Combine(_basePath, token, filename);
    FileInfo fi = new(filePath);
    if (!fi.Exists)
      throw new FileNotFoundException($"File not found: {filePath}", filePath);

    return Task.FromResult((ulong)fi.Length);
  }

  public Task<bool> ExistsAsync(string token, CancellationToken ct = default)
  {
    StoragePath.EnsureSafeSegment(token, nameof(token));
    string dir = Path.Combine(_basePath, token);
    return Task.FromResult(Directory.Exists(dir));
  }

  public Task<bool> TryReserveTokenAsync(string token, CancellationToken ct = default)
  {
    StoragePath.EnsureSafeSegment(token, nameof(token));
    ct.ThrowIfCancellationRequested();
    string dir = Path.Combine(_basePath, token);
    string reservationPath = Path.Combine(dir, ".upload-reservation");
    if (File.Exists(reservationPath) &&
        File.GetLastWriteTimeUtc(reservationPath) < DateTime.UtcNow - ReservationLifetime)
      File.Delete(reservationPath);

    if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
      return Task.FromResult(false);

    Directory.CreateDirectory(dir);
    try
    {
      using FileStream reservation = new(reservationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      return Task.FromResult(true);
    }
    catch (IOException)
    {
      return Task.FromResult(false);
    }
  }

  public Task ReleaseTokenAsync(string token, CancellationToken ct = default)
  {
    StoragePath.EnsureSafeSegment(token, nameof(token));
    string dir = Path.Combine(_basePath, token);
    File.Delete(Path.Combine(dir, ".upload-reservation"));
    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
      Directory.Delete(dir);
    return Task.CompletedTask;
  }

  public Task DeleteAsync(string token, string filename, CancellationToken ct = default)
  {
    StoragePath.EnsureSafeSegment(token, nameof(token));
    StoragePath.EnsureSafeSegment(filename, nameof(filename));
    string filePath = Path.Combine(_basePath, token, filename);
    if (File.Exists(filePath))
      File.Delete(filePath);

    string dir = Path.Combine(_basePath, token);
    if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
      Directory.Delete(dir);

    return Task.CompletedTask;
  }

  public Task PurgeAsync(TimeSpan maxAge, CancellationToken ct = default)
  {
    if (!Directory.Exists(_basePath))
      return Task.CompletedTask;

    DateTime cutoff = DateTime.UtcNow - maxAge;

    foreach (string tokenDir in Directory.GetDirectories(_basePath))
    {
      string reservationPath = Path.Combine(tokenDir, ".upload-reservation");
      if (File.Exists(reservationPath))
      {
        if (File.GetLastWriteTimeUtc(reservationPath) >= DateTime.UtcNow - ReservationLifetime)
          continue;
        File.Delete(reservationPath);
      }

      string[] files = Directory.GetFiles(tokenDir);
      IEnumerable<string> payloads = files.Where(file =>
        !file.EndsWith(".metadata", StringComparison.Ordinal) || File.Exists($"{file}.metadata"));
      foreach (string payload in payloads)
      {
        ct.ThrowIfCancellationRequested();
        FileInfo fileInfo = new(payload);
        if (fileInfo.CreationTimeUtc >= cutoff)
          continue;

        fileInfo.Delete();
        File.Delete($"{payload}.metadata");
      }

      foreach (string metadata in Directory.GetFiles(tokenDir, "*.metadata"))
      {
        if (File.Exists($"{metadata}.metadata"))
          continue;

        string payload = metadata[..^".metadata".Length];
        if (!File.Exists(payload))
          File.Delete(metadata);
      }

      if (Directory.GetFiles(tokenDir).Length == 0 && Directory.GetDirectories(tokenDir).Length == 0)
        Directory.Delete(tokenDir);
    }

    return Task.CompletedTask;
  }

  public bool IsNotExist(Exception ex)
  {
    return ex is FileNotFoundException;
  }

  /// <summary>
  /// A stream wrapper that limits the number of bytes that can be read from the underlying stream.
  /// Used for range request support.
  /// </summary>
  private sealed class LimitedStream : Stream
  {
    private readonly Stream _inner;
    private long _remaining;

    public LimitedStream(Stream inner, long limit)
    {
      _inner = inner;
      _remaining = limit;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      if (_remaining <= 0) return 0;
      int toRead = (int)Math.Min(count, _remaining);
      int read = _inner.Read(buffer, offset, toRead);
      _remaining -= read;
      return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
      if (_remaining <= 0) return 0;
      int toRead = (int)Math.Min(count, _remaining);
      int read = await _inner.ReadAsync(buffer, offset, toRead, cancellationToken);
      _remaining -= read;
      return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      if (_remaining <= 0) return 0;
      int toRead = (int)Math.Min(buffer.Length, _remaining);
      int read = await _inner.ReadAsync(buffer[..toRead], cancellationToken);
      _remaining -= read;
      return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
      throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        _inner.Dispose();
      base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
      await _inner.DisposeAsync();
      GC.SuppressFinalize(this);
    }
  }
}
