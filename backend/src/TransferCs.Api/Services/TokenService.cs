using System.Security.Cryptography;

namespace TransferCs.Api.Services;

public static class TokenService
{
    private const string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static string Generate(int length)
    {
        return string.Create(length, (object?)null, (span, _) =>
        {
            Span<byte> randomBytes = stackalloc byte[length];
            RandomNumberGenerator.Fill(randomBytes);
            for (var i = 0; i < length; i++)
                span[i] = Chars[randomBytes[i] % Chars.Length];
        });
    }
}
