using System.Security.Cryptography;
using System.Text;
using TransferCs.Api.Services;

namespace TransferCs.Api.Tests.Services;

public class UnlockCookieTests
{
  private static readonly string _passwordHash = new DownloadPasswordHasher(1_000).Hash("secret");
  private static readonly DateTimeOffset _now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void GetName_UsesTruncatedSha256OfTokenAndFilename()
  {
    string expected = "tcs_unlock_" +
      Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("token/file.txt")))[..16];

    Assert.Equal(expected, UnlockCookie.GetName("token", "file.txt"));
    Assert.NotEqual(UnlockCookie.GetName("token", "file.txt"), UnlockCookie.GetName("token", "other.txt"));
  }

  [Fact]
  public void Create_UsesExpiryAndBase64UrlHmacOfStoredHash()
  {
    DateTimeOffset expiry = _now.AddHours(12);
    Assert.True(DownloadPasswordHasher.TryGetHashBytes(_passwordHash, out byte[] key));
    byte[] mac = HMACSHA256.HashData(key,
      Encoding.UTF8.GetBytes($"unlock\ntoken\nfile.txt\n{expiry.ToUnixTimeSeconds()}"));

    string value = UnlockCookie.Create(_passwordHash, "token", "file.txt", expiry);

    Assert.Equal($"{expiry.ToUnixTimeSeconds()}.{System.Buffers.Text.Base64Url.EncodeToString(mac)}", value);
  }

  [Fact]
  public void Validate_AcceptsUnexpiredCookie()
  {
    string value = UnlockCookie.Create(_passwordHash, "token", "file.txt", _now.AddHours(1));

    Assert.True(UnlockCookie.Validate(value, _passwordHash, "token", "file.txt", _now));
  }

  [Fact]
  public void Validate_RejectsExpiredCookie()
  {
    string value = UnlockCookie.Create(_passwordHash, "token", "file.txt", _now);

    Assert.False(UnlockCookie.Validate(value, _passwordHash, "token", "file.txt", _now));
    Assert.False(UnlockCookie.Validate(value, _passwordHash, "token", "file.txt", _now.AddSeconds(1)));
  }

  [Fact]
  public void Validate_RejectsCookieForOtherFileOrPassword()
  {
    string value = UnlockCookie.Create(_passwordHash, "token", "file.txt", _now.AddHours(1));
    string otherHash = new DownloadPasswordHasher(1_000).Hash("secret");

    Assert.False(UnlockCookie.Validate(value, _passwordHash, "token", "other.txt", _now));
    Assert.False(UnlockCookie.Validate(value, _passwordHash, "other", "file.txt", _now));
    Assert.False(UnlockCookie.Validate(value, otherHash, "token", "file.txt", _now));
  }

  [Fact]
  public void Validate_RejectsExtendedExpiry()
  {
    string value = UnlockCookie.Create(_passwordHash, "token", "file.txt", _now.AddHours(1));
    string forged = $"{_now.AddYears(1).ToUnixTimeSeconds()}{value[value.IndexOf('.')..]}";

    Assert.False(UnlockCookie.Validate(forged, _passwordHash, "token", "file.txt", _now));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("abc")]
  [InlineData("1.")]
  [InlineData(".abc")]
  [InlineData("notanumber.abc")]
  [InlineData("99999999999.!!!")]
  [InlineData("99999999999999999999.abc")]
  public void Validate_RejectsMalformedValues(string? value)
  {
    Assert.False(UnlockCookie.Validate(value, _passwordHash, "token", "file.txt", _now));
  }

  [Fact]
  public void Validate_RejectsMalformedStoredHash()
  {
    string value = UnlockCookie.Create(_passwordHash, "token", "file.txt", _now.AddHours(1));

    Assert.False(UnlockCookie.Validate(value, "invalid", "token", "file.txt", _now));
  }
}
