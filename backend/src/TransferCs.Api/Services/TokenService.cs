using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TransferCs.Api.Services;

public static partial class TokenService
{
  private const string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";

  [GeneratedRegex("^[a-z0-9-]+$")]
  private static partial Regex CustomTokenPattern();

  public static string Generate(int length)
  {
    return string.Create(length, (object?)null, (span, _) =>
    {
      Span<byte> randomBytes = stackalloc byte[length];
      RandomNumberGenerator.Fill(randomBytes);
      for (int i = 0; i < length; i++)
        span[i] = Chars[randomBytes[i] % Chars.Length];
    });
  }

  public static string? ValidateCustomToken(string token)
  {
    if (token.Length < 4)
      return "Token must be at least 4 characters";
    if (!CustomTokenPattern().IsMatch(token))
      return "Token may only contain a-z, 0-9, and hyphens";
    return null;
  }
}