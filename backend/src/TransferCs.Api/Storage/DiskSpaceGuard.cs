using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Storage;

public sealed class DiskSpaceGuard : IDisposable
{
  private const int BufferSize = 81920;
  private const long AllocationMarginBytes = 64 * 1024;
  private readonly IDiskSpaceProbe _probe;
  private readonly long _minimumBytes;
  private readonly string _tempPath;
  internal SemaphoreSlim WriteLock { get; } = new(1, 1);

  public DiskSpaceGuard(IOptions<TransferCsOptions> options, IDiskSpaceProbe probe)
  {
    _probe = probe;
    _minimumBytes = checked(options.Value.MinFreeDiskSpaceMb * 1024 * 1024);
    ArgumentOutOfRangeException.ThrowIfNegative(_minimumBytes);
    _tempPath = options.Value.ResolvedTempPath;
  }

  public void EnsureAvailable(string path, long bytes = 0)
  {
    if (_minimumBytes == 0)
      return;

    long available = _probe.GetAvailableBytes(path);
    if (available < _minimumBytes || available - _minimumBytes < AllocationMarginBytes ||
        bytes > available - _minimumBytes - AllocationMarginBytes)
      throw new InsufficientStorageException(path);
  }

  public Stream CreateFile(string path, FileOptions fileOptions = FileOptions.None)
  {
    string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
    Directory.CreateDirectory(directory);
    EnsureAvailable(directory);
    FileStream file = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
      _minimumBytes == 0 ? BufferSize : 1, fileOptions);
    // Buffer small ZIP/PGP writes; the inner stream checks space immediately before each disk write.
    return _minimumBytes == 0 ? file : new BufferedStream(new DiskSpaceWriteStream(file, this), BufferSize);
  }

  public Stream CreateTemporaryFile(string prefix)
  {
    string path = Path.Combine(_tempPath, $"{prefix}-{Guid.NewGuid():N}");
    return CreateFile(path, FileOptions.DeleteOnClose);
  }

  public void Dispose() => WriteLock.Dispose();
}
