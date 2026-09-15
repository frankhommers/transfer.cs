using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Storage;
using TransferCs.Api.Tests.Helpers;

namespace TransferCs.Api.Tests.Storage;

public sealed class DiskSpaceGuardTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), $"disk-guard-{Guid.NewGuid():N}");

  [Fact]
  public async Task ConcurrentWriters_CannotSpendTheSameFreeSpaceAsync()
  {
    Directory.CreateDirectory(_directory);
    TestDiskSpaceProbe probe = new()
    {
      AvailableBytes = _ => 1024 * 1024 + 64 * 1024 + 200_000 -
        Directory.GetFiles(_directory).Sum(path => new FileInfo(path).Length)
    };
    using DiskSpaceGuard guard = CreateGuard(probe);

    Task<bool>[] writes = Enumerable.Range(0, 10).Select(async index =>
    {
      try
      {
        await using Stream file = guard.CreateFile(Path.Combine(_directory, index.ToString()));
        await file.WriteAsync(new byte[100_000]);
        await file.FlushAsync();
        return true;
      }
      catch (InsufficientStorageException)
      {
        return false;
      }
    }).ToArray();

    bool[] results = await Task.WhenAll(writes);
    Assert.Equal(2, results.Count(result => result));
    Assert.Equal(200_000, Directory.GetFiles(_directory).Sum(path => new FileInfo(path).Length));
  }

  [Fact]
  public async Task SpaceDropsDuringBufferedWrites_TemporaryFileIsRemovedAsync()
  {
    TestDiskSpaceProbe probe = new();
    using DiskSpaceGuard guard = CreateGuard(probe);

    await Assert.ThrowsAsync<InsufficientStorageException>(async () =>
    {
      await using Stream file = guard.CreateTemporaryFile("test");
      file.WriteByte(42);
      await file.FlushAsync();
      Assert.Equal(1, file.Length);
      probe.AvailableBytes = _ => 0;
      file.WriteByte(43);
      await file.FlushAsync();
    });

    Assert.Empty(Directory.GetFiles(_directory));
    probe.AvailableBytes = _ => long.MaxValue;
    await using Stream retry = guard.CreateTemporaryFile("retry");
    await retry.WriteAsync(new byte[100_000]);
    await retry.FlushAsync();
    Assert.Equal(100_000, retry.Length);
  }

  [Fact]
  public async Task DisabledGuard_DoesNotQueryDiskSpaceAsync()
  {
    TestDiskSpaceProbe probe = new() { AvailableBytes = _ => throw new IOException("Unavailable probe") };
    using DiskSpaceGuard guard = CreateGuard(probe, 0);
    await using Stream file = guard.CreateTemporaryFile("test");
    await file.WriteAsync(new byte[100_000]);
    await file.FlushAsync();
    Assert.Empty(probe.CheckedPaths);
  }

  [Fact]
  public void Probe_UsesAnExistingNestedPath()
  {
    Directory.CreateDirectory(Path.Combine(_directory, "nested"));
    DiskSpaceProbe probe = new();
    Assert.True(probe.GetAvailableBytes(Path.Combine(_directory, "nested")) > 0);
  }

  private DiskSpaceGuard CreateGuard(IDiskSpaceProbe probe, long minimumMb = 1) => new(
    Options.Create(new TransferCsOptions { MinFreeDiskSpaceMb = minimumMb, TempPath = _directory }), probe);

  public void Dispose()
  {
    if (Directory.Exists(_directory))
      Directory.Delete(_directory, true);
  }
}
