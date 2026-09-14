using TransferCs.Api.Helpers;

namespace TransferCs.Api.Tests.Helpers;

public class HttpDigestHelperTests
{
  private const string Hex = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
  private const string Header = "sha-256=:n4bQgYhMfWWaL+qgxVrQFaO/Txs rC4Is0V1sFbDwCgg=:";

  [Fact]
  public void Sha256_UsesStructuredFieldByteSequence()
  {
    string expected = Header.Replace(" ", "");
    Assert.Equal(expected, HttpDigestHelper.Format(Hex));
    Assert.True(HttpDigestHelper.TryParse(expected, out string parsed));
    Assert.Equal(Hex, parsed);
  }

  [Theory]
  [InlineData("")]
  [InlineData("sha-256=:")]
  [InlineData("sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
  [InlineData("sha-256=:dGVzdA==:")]
  [InlineData(Header)]
  [InlineData("sha-512=:dGVzdA==:")]
  public void MalformedOrUnsupportedDigest_IsRejected(string header)
  {
    Assert.False(HttpDigestHelper.TryParse(header, out _));
  }
}
