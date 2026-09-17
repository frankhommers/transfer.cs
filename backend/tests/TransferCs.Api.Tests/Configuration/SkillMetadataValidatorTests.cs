using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Tests.Configuration;

public sealed class SkillMetadataValidatorTests
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
    ValidateOptionsResult result = new SkillMetadataValidator().Validate(null, options);

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
    Assert.True(new SkillMetadataValidator().Validate(null, options).Succeeded);
  }

  [Theory]
  [InlineData(64, true)]
  [InlineData(65, false)]
  public void NameLength_IsLimited(int length, bool accepted)
  {
    TransferCsOptions options = new() { SkillName = new string('a', length) };
    Assert.Equal(accepted, new SkillMetadataValidator().Validate(null, options).Succeeded);
  }

  [Theory]
  [InlineData("")]
  [InlineData(" \n\t ")]
  [InlineData("Share with <team>")]
  [InlineData("Share\u0000files")]
  [InlineData("Share\u007ffiles")]
  [InlineData("Share\ufffffiles")]
  public void InvalidDescriptions_ReportGlobalAndSiteConfigurationPaths(string description)
  {
    TransferCsOptions options = new()
    {
      SkillDescription = description,
      Sites = new() { ["alpha"] = new() { SkillDescription = description } }
    };
    ValidateOptionsResult result = new SkillMetadataValidator().Validate(null, options);

    Assert.True(result.Failed);
    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:SkillDescription "));
    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:Sites:alpha:SkillDescription "));
  }

  [Theory]
  [InlineData("Team \"Alpha\": privé # bestanden")]
  [InlineData("Share files 🚀")]
  [InlineData("First line\r\nSecond line\n")]
  [InlineData("\n  Indented text\n\n")]
  [InlineData("null")]
  [InlineData("Use {{Title}} literally")]
  public void PlainTextDescriptions_AreAccepted(string description)
  {
    TransferCsOptions options = new() { SkillDescription = description };
    Assert.True(new SkillMetadataValidator().Validate(null, options).Succeeded);
  }

  [Theory]
  [InlineData(1024, true)]
  [InlineData(1025, false)]
  public void DescriptionLength_IsLimited(int length, bool accepted)
  {
    TransferCsOptions options = new() { SkillDescription = new string('a', length) };
    Assert.Equal(accepted, new SkillMetadataValidator().Validate(null, options).Succeeded);
  }
}
