namespace TransferCs.Api.Storage;

public sealed class DiskSpaceProbe : IDiskSpaceProbe
{
  public long GetAvailableBytes(string path) => new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace;
}
