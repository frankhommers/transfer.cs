using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Tests.Configuration;

public sealed class SkillNameValidatorTests
{
  [Theory]
  [InlineData("")]
  [InlineData("Company-Files")]
  [InlineData("company files")]
  [InlineData("transfer.cs")]
  [InlineData("-files")]
  [InlineData("files-")]
  [InlineData("company--files")]
  [InlineData("files\n")]
  [InlineData("files\"\nother: injected")]
  public void InvalidNames_ReportGlobalAndSiteConfigurationPaths(string name)
  {
    TransferCsOptions options = new()
    {
      SkillName = name,
      Sites = new() { ["alpha"] = new() { SkillName = name } }
    };
    ValidateOptionsResult result = new SkillNameValidator().Validate(null, options);

    Assert.True(result.Failed);
    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:SkillName "));
    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:Sites:alpha:SkillName "));
  }

  [Theory]
  [InlineData("transfer-cs")]
  [InlineData("company-123")]
  [InlineData("x")]
  [InlineData("123")]
  [InlineData("true")]
  [InlineData("null")]
  public void ValidNames_AllowGlobalInheritance(string name)
  {
    TransferCsOptions options = new() { SkillName = name, Sites = new() { ["alpha"] = new() } };
    Assert.True(new SkillNameValidator().Validate(null, options).Succeeded);
  }

  [Theory]
  [InlineData(64, true)]
  [InlineData(65, false)]
  public void NameLength_IsLimited(int length, bool accepted)
  {
    TransferCsOptions options = new() { SkillName = new string('a', length) };
    Assert.Equal(accepted, new SkillNameValidator().Validate(null, options).Succeeded);
  }
}
