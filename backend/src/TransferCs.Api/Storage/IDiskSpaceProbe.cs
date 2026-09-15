namespace TransferCs.Api.Storage;

public interface IDiskSpaceProbe
{
  long GetAvailableBytes(string path);
}
