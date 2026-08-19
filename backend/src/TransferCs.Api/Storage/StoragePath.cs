namespace TransferCs.Api.Storage;

public static class StoragePath
{
  public static bool IsSafeSegment(string value) =>
    !string.IsNullOrWhiteSpace(value) &&
    value is not "." and not ".." &&
    !value.Contains('/') &&
    !value.Contains('\\') &&
    !Path.IsPathRooted(value);

  public static void EnsureSafeSegment(string value, string parameterName)
  {
    if (!IsSafeSegment(value))
      throw new ArgumentException("Storage keys must be a single path segment.", parameterName);
  }
}
