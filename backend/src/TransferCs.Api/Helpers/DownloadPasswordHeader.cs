using Microsoft.Extensions.Primitives;
using TransferCs.Api.Services;

namespace TransferCs.Api.Helpers;

public static class DownloadPasswordHeader
{
  public const int MaxLength = 1024;

  public static string? Validate(StringValues values)
  {
    string value = values.ToString();
    return values.Count != 1 || string.IsNullOrWhiteSpace(value) || value.Length > MaxLength
      ? $"{DownloadAuthorizer.HeaderName} must be a single value of 1 to {MaxLength} characters " +
        "that is not only whitespace."
      : null;
  }
}
