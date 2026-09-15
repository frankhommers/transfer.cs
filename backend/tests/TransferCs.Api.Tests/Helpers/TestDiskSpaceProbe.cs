using System.Collections.Concurrent;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Tests.Helpers;

public sealed class TestDiskSpaceProbe : IDiskSpaceProbe
{
  public Func<string, long> AvailableBytes { get; set; } = _ => long.MaxValue;
  public ConcurrentBag<string> CheckedPaths { get; } = [];

  public long GetAvailableBytes(string path)
  {
    CheckedPaths.Add(path);
    return AvailableBytes(path);
  }
}
