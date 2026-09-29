using System.Text;
using Microsoft.Extensions.Primitives;
using TransferCs.Api.Services;

namespace TransferCs.Api.Helpers;

/// <summary>
/// Header values cannot carry non-ASCII text or leading/trailing whitespace reliably, so browsers send the
/// password base64-encoded (UTF-8) in <see cref="Base64Name"/>; CLI users can keep using the plain header.
/// </summary>
public static class DownloadPasswordHeader
{
  public const string Name = DownloadAuthorizer.HeaderName;
  public const string Base64Name = "Download-Password-Base64";
  public const int MaxLength = 1024;
  private static readonly UTF8Encoding _strictUtf8 = new(false, true);

  public static bool IsPresent(IHeaderDictionary headers) =>
    headers.ContainsKey(Name) || headers.ContainsKey(Base64Name);

  public static string? Read(IHeaderDictionary headers, out string? password)
  {
    password = null;
    bool hasPlain = headers.TryGetValue(Name, out StringValues plain);
    bool hasEncoded = headers.TryGetValue(Base64Name, out StringValues encoded);
    if (hasPlain && hasEncoded)
      return $"Send either {Name} or {Base64Name}, not both.";
    if (hasPlain)
      return TryAccept(plain.Count == 1 ? plain.ToString() : null, out password)
        ? null
        : $"{Name} must be a single value of 1 to {MaxLength} characters.";
    if (hasEncoded)
      return TryAccept(encoded.Count == 1 ? Decode(encoded.ToString()) : null, out password)
        ? null
        : $"{Base64Name} must be a single base64-encoded UTF-8 value of 1 to {MaxLength} characters.";
    return null;
  }

  private static bool TryAccept(string? value, out string? password)
  {
    password = value is { Length: > 0 and <= MaxLength } ? value : null;
    return password != null;
  }

  private static string? Decode(string value)
  {
    try
    {
      return _strictUtf8.GetString(Convert.FromBase64String(value));
    }
    catch (FormatException)
    {
      return null;
    }
    catch (DecoderFallbackException)
    {
      return null;
    }
  }
}
