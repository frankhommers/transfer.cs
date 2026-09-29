using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using TransferCs.Api.Helpers;

namespace TransferCs.Api.Tests.Helpers;

public class DownloadPasswordHeaderTests
{
  [Fact]
  public void Read_ReturnsNothingWhenAbsent()
  {
    Assert.Null(DownloadPasswordHeader.Read(new HeaderDictionary(), out string? password));
    Assert.Null(password);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void Read_RejectsEmptyPlainValue(string value)
  {
    HeaderDictionary headers = new() { [DownloadPasswordHeader.Name] = value.Trim() };
    Assert.NotNull(DownloadPasswordHeader.Read(headers, out _));
  }

  [Fact]
  public void Read_RejectsTooLongAndMultipleValues()
  {
    Assert.NotNull(DownloadPasswordHeader.Read(
      new HeaderDictionary { [DownloadPasswordHeader.Name] = new string('a', 1025) }, out _));
    Assert.NotNull(DownloadPasswordHeader.Read(
      new HeaderDictionary { [DownloadPasswordHeader.Name] = new StringValues(["one", "two"]) }, out _));
    Assert.NotNull(DownloadPasswordHeader.Read(
      new HeaderDictionary { [DownloadPasswordHeader.Base64Name] = Encode(new string('a', 1025)) }, out _));
  }

  [Theory]
  [InlineData("x")]
  [InlineData("a  b")]
  public void Read_AcceptsPlainValueVerbatim(string value)
  {
    HeaderDictionary headers = new() { [DownloadPasswordHeader.Name] = value };
    Assert.Null(DownloadPasswordHeader.Read(headers, out string? password));
    Assert.Equal(value, password);
  }

  [Theory]
  [InlineData(" padded ")]
  [InlineData("   ")]
  [InlineData("café ☕ 1lI0O \"'$<>&")]
  public void Read_DecodesBase64ValueVerbatim(string value)
  {
    HeaderDictionary headers = new() { [DownloadPasswordHeader.Base64Name] = Encode(value) };
    Assert.Null(DownloadPasswordHeader.Read(headers, out string? password));
    Assert.Equal(value, password);
  }

  [Theory]
  [InlineData("not base64!")]
  [InlineData("")]
  [InlineData("/w==")]
  public void Read_RejectsInvalidBase64OrUtf8(string value)
  {
    HeaderDictionary headers = new() { [DownloadPasswordHeader.Base64Name] = value };
    Assert.NotNull(DownloadPasswordHeader.Read(headers, out _));
  }

  [Fact]
  public void Read_RejectsBothHeaders()
  {
    HeaderDictionary headers = new()
    {
      [DownloadPasswordHeader.Name] = "x",
      [DownloadPasswordHeader.Base64Name] = Encode("x")
    };
    Assert.NotNull(DownloadPasswordHeader.Read(headers, out _));
  }

  private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
