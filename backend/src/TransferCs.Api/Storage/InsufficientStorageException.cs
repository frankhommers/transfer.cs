namespace TransferCs.Api.Storage;

public sealed class InsufficientStorageException(string path) : IOException(
  "There is not enough free space on the server. Please try again later.")
{
  public string StoragePath { get; } = path;
}
