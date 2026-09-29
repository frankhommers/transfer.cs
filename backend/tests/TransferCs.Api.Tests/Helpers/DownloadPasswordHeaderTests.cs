using Microsoft.Extensions.Primitives;
using TransferCs.Api.Helpers;

namespace TransferCs.Api.Tests.Helpers;

public class DownloadPasswordHeaderTests
{
  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("\t")]
  [InlineData(" ")]
  public void Validate_RejectsEmptyOrWhitespace(string value)
  {
    Assert.NotNull(DownloadPasswordHeader.Validate(new StringValues(value)));
  }

  [Fact]
  public void Validate_RejectsTooLongAndMultipleValues()
  {
    Assert.NotNull(DownloadPasswordHeader.Validate(new StringValues(new string('a', 1025))));
    Assert.NotNull(DownloadPasswordHeader.Validate(new StringValues(["one", "two"])));
  }

  [Theory]
  [InlineData("x")]
  [InlineData(" padded ")]
  [InlineData("a  b")]
  public void Validate_AcceptsValueVerbatim(string value)
  {
    Assert.Null(DownloadPasswordHeader.Validate(new StringValues(value)));
    Assert.Null(DownloadPasswordHeader.Validate(new StringValues(new string('a', 1024))));
  }
}
