using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TransferCs.Api.Services;

public static class UnlockCookie
{
  private const string NamePrefix = "tcs_unlock_";

  public static string GetName(string token, string filename) =>
    NamePrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{token}/{filename}")))[..16];

  public static string Create(string passwordHash, string token, string filename, DateTimeOffset expiry)
  {
    if (!DownloadPasswordHasher.TryGetHashBytes(passwordHash, out byte[] key))
      throw new ArgumentException("Invalid password hash.", nameof(passwordHash));
    long expirySeconds = expiry.ToUnixTimeSeconds();
    return $"{expirySeconds.ToString(CultureInfo.InvariantCulture)}." +
           Base64Url.EncodeToString(ComputeMac(key, token, filename, expirySeconds));
  }

  public static bool Validate(string? value, string passwordHash, string token, string filename, DateTimeOffset now)
  {
    if (string.IsNullOrEmpty(value))
      return false;
    int separator = value.IndexOf('.');
    if (separator <= 0 ||
        !long.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture,
          out long expirySeconds) ||
        expirySeconds <= now.ToUnixTimeSeconds() ||
        !DownloadPasswordHasher.TryGetHashBytes(passwordHash, out byte[] key))
      return false;

    byte[] supplied;
    try
    {
      supplied = Base64Url.DecodeFromChars(value.AsSpan(separator + 1));
    }
    catch (FormatException)
    {
      return false;
    }

    return CryptographicOperations.FixedTimeEquals(supplied, ComputeMac(key, token, filename, expirySeconds));
  }

  private static byte[] ComputeMac(byte[] key, string token, string filename, long expirySeconds) =>
    HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(
      $"unlock\n{token}\n{filename}\n{expirySeconds.ToString(CultureInfo.InvariantCulture)}"));
}
