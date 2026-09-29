using System.Globalization;
using System.Security.Cryptography;

namespace TransferCs.Api.Services;

public sealed class DownloadPasswordHasher
{
  public const int DefaultIterations = 600_000;
  private const string Algorithm = "pbkdf2-sha256";
  private const int SaltSize = 16;
  private const int HashSize = 32;
  private readonly int _iterations;

  public DownloadPasswordHasher() : this(DefaultIterations)
  {
  }

  public DownloadPasswordHasher(int iterations)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
    _iterations = iterations;
  }

  public string Hash(string password)
  {
    byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
    byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashSize);
    return string.Join('$', Algorithm, _iterations.ToString(CultureInfo.InvariantCulture),
      Convert.ToBase64String(salt), Convert.ToBase64String(hash));
  }

  public bool Verify(string password, string storedHash)
  {
    if (!TryParse(storedHash, out int iterations, out byte[] salt, out byte[] expected))
      return false;
    byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
    return CryptographicOperations.FixedTimeEquals(actual, expected);
  }

  public static bool TryGetHashBytes(string storedHash, out byte[] hash) =>
    TryParse(storedHash, out _, out _, out hash);

  private static bool TryParse(string storedHash, out int iterations, out byte[] salt, out byte[] hash)
  {
    iterations = 0;
    salt = [];
    hash = [];
    string[] parts = storedHash.Split('$');
    if (parts.Length != 4 || parts[0] != Algorithm ||
        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations) || iterations <= 0)
      return false;

    try
    {
      salt = Convert.FromBase64String(parts[2]);
      hash = Convert.FromBase64String(parts[3]);
    }
    catch (FormatException)
    {
      return false;
    }

    return salt.Length == SaltSize && hash.Length == HashSize;
  }
}
