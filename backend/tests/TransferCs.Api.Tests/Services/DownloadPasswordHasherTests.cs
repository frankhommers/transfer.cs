using TransferCs.Api.Services;

namespace TransferCs.Api.Tests.Services;

public class DownloadPasswordHasherTests
{
  private readonly DownloadPasswordHasher _hasher = new(1_000);

  [Fact]
  public void DefaultIterations_MatchesDesign()
  {
    Assert.Equal(600_000, DownloadPasswordHasher.DefaultIterations);
  }

  [Fact]
  public void Hash_UsesVersionedFormatWithSaltAndHashLengths()
  {
    string hash = _hasher.Hash("secret");

    string[] parts = hash.Split('$');
    Assert.Equal(4, parts.Length);
    Assert.Equal("pbkdf2-sha256", parts[0]);
    Assert.Equal("1000", parts[1]);
    Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
    Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
  }

  [Fact]
  public void Hash_UsesRandomSalt()
  {
    Assert.NotEqual(_hasher.Hash("secret"), _hasher.Hash("secret"));
  }

  [Theory]
  [InlineData("secret")]
  [InlineData(" padded ")]
  [InlineData("pässwörd 🔒")]
  public void Verify_AcceptsExactPassword(string password)
  {
    Assert.True(_hasher.Verify(password, _hasher.Hash(password)));
  }

  [Theory]
  [InlineData("Secret")]
  [InlineData("secret ")]
  [InlineData(" secret")]
  [InlineData("")]
  public void Verify_RejectsDifferentPassword(string password)
  {
    Assert.False(_hasher.Verify(password, _hasher.Hash("secret")));
  }

  [Fact]
  public void Verify_UsesIterationCountFromStoredHash()
  {
    string stored = new DownloadPasswordHasher(2_000).Hash("secret");

    Assert.True(_hasher.Verify("secret", stored));
  }

  [Theory]
  [InlineData("")]
  [InlineData("plain")]
  [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAA==")]
  [InlineData("pbkdf2-sha512$1000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
  [InlineData("pbkdf2-sha256$0$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
  [InlineData("pbkdf2-sha256$abc$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
  [InlineData("pbkdf2-sha256$1000$not-base64$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
  [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
  public void Verify_RejectsMalformedStoredHash(string stored)
  {
    Assert.False(_hasher.Verify("secret", stored));
  }

  [Fact]
  public void TryGetHashBytes_ReturnsDerivedKey()
  {
    string stored = _hasher.Hash("secret");

    Assert.True(DownloadPasswordHasher.TryGetHashBytes(stored, out byte[] hash));
    Assert.Equal(Convert.FromBase64String(stored.Split('$')[3]), hash);
    Assert.False(DownloadPasswordHasher.TryGetHashBytes("invalid", out _));
  }

  [Fact]
  public void Constructor_RejectsNonPositiveIterations()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new DownloadPasswordHasher(0));
  }
}
