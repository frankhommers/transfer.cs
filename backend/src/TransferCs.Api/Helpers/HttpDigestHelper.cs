namespace TransferCs.Api.Helpers;

public static class HttpDigestHelper
{
  public static string Format(string sha256Hex) =>
    $"sha-256=:{Convert.ToBase64String(Convert.FromHexString(sha256Hex))}:";

  public static bool TryParse(string value, out string sha256Hex)
  {
    sha256Hex = "";
    string trimmed = value.Trim();
    const string prefix = "sha-256=:";
    if (trimmed.Length <= prefix.Length ||
        !trimmed.StartsWith(prefix, StringComparison.Ordinal) || !trimmed.EndsWith(':'))
    {
      return false;
    }

    ReadOnlySpan<char> encoded = trimmed.AsSpan(prefix.Length, trimmed.Length - prefix.Length - 1);
    if (encoded.Length != 44 || encoded.IndexOfAny(" \t\r\n") >= 0)
    {
      return false;
    }

    Span<byte> digest = stackalloc byte[32];
    if (!Convert.TryFromBase64Chars(encoded, digest, out int written) || written != digest.Length)
    {
      return false;
    }

    sha256Hex = Convert.ToHexStringLower(digest);
    return true;
  }
}
