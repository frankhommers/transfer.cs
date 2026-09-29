using TransferCs.Api.Helpers;

namespace TransferCs.Api.Tests.Helpers;

public class HeaderValueHelperTests
{
  [Theory]
  [InlineData("Alpha files", "Alpha files")]
  [InlineData("Café ☕ files", "Cafe files")]
  [InlineData("  Evil\r\nX-Injected: 1\t", "EvilX-Injected: 1")]
  [InlineData("文件", "")]
  public void ToSafeValue_KeepsOnlyVisibleAscii(string value, string expected) =>
    Assert.Equal(expected, HeaderValueHelper.ToSafeValue(value));

  [Fact]
  public void ToQuotedString_EscapesQuotesAndBackslashes() =>
    Assert.Equal("\"a \\\"b\\\" \\\\ c\"", HeaderValueHelper.ToQuotedString("a \"b\" \\ c"));
}
