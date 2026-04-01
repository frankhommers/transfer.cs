using System.Security.Cryptography;
using System.Text;

namespace TransferCs.Api.Services;

public class HtpasswdService
{
  private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

  public HtpasswdService(string filePath)
  {
    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
      return;

    foreach (string line in File.ReadAllLines(filePath))
    {
      string trimmed = line.Trim();
      if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
        continue;

      int colonIndex = trimmed.IndexOf(':');
      if (colonIndex <= 0) continue;

      string username = trimmed[..colonIndex];
      string hash = trimmed[(colonIndex + 1)..];
      _entries[username] = hash;
    }
  }

  public bool Validate(string username, string password)
  {
    if (!_entries.TryGetValue(username, out string? storedHash))
      return false;

    // Support {SHA} prefix (SHA1 base64)
    if (storedHash.StartsWith("{SHA}", StringComparison.OrdinalIgnoreCase))
    {
      string expectedBase64 = storedHash[5..];
      byte[] actualHash = SHA1.HashData(Encoding.UTF8.GetBytes(password));
      string actualBase64 = Convert.ToBase64String(actualHash);
      return string.Equals(expectedBase64, actualBase64, StringComparison.Ordinal);
    }

    // Plain text fallback
    return storedHash == password;
  }
}