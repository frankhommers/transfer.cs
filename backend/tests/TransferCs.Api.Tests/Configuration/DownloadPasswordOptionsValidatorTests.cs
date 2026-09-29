using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Tests.Configuration;

public sealed class DownloadPasswordOptionsValidatorTests
{
  [Fact]
  public void Defaults_MatchDesignAndAreValid()
  {
    TransferCsOptions options = new() { Sites = new() { ["alpha"] = new() } };

    Assert.Equal(50, options.DownloadPasswordMaxAttempts);
    Assert.Equal(15, options.DownloadPasswordAttemptWindowMinutes);
    Assert.Equal(12, options.DownloadPasswordUnlockHours);
    Assert.True(new DownloadPasswordOptionsValidator().Validate(null, options).Succeeded);
  }

  [Theory]
  [InlineData(0, 1, 1, true)]
  [InlineData(-1, 15, 12, false)]
  [InlineData(50, 0, 12, false)]
  [InlineData(50, 15, 0, false)]
  [InlineData(50, 525_600, 87_600, true)]
  [InlineData(50, 525_601, 12, false)]
  [InlineData(50, 15, 87_601, false)]
  public void GlobalValues_AreValidated(int maxAttempts, int windowMinutes, int unlockHours, bool accepted)
  {
    TransferCsOptions options = new()
    {
      DownloadPasswordMaxAttempts = maxAttempts,
      DownloadPasswordAttemptWindowMinutes = windowMinutes,
      DownloadPasswordUnlockHours = unlockHours
    };

    Assert.Equal(accepted, new DownloadPasswordOptionsValidator().Validate(null, options).Succeeded);
  }

  [Fact]
  public void SiteOverrides_ReportSiteConfigurationPaths()
  {
    TransferCsOptions options = new()
    {
      Sites = new()
      {
        ["alpha"] = new()
        {
          DownloadPasswordMaxAttempts = -1,
          DownloadPasswordAttemptWindowMinutes = 0,
          DownloadPasswordUnlockHours = 0
        }
      }
    };

    ValidateOptionsResult result = new DownloadPasswordOptionsValidator().Validate(null, options);

    Assert.True(result.Failed);
    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:Sites:alpha:DownloadPasswordMaxAttempts "));
    Assert.Contains(result.Failures!,
      error => error.StartsWith("TransferCs:Sites:alpha:DownloadPasswordAttemptWindowMinutes "));
    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:Sites:alpha:DownloadPasswordUnlockHours "));
  }

  [Fact]
  public void InvalidGlobalValues_ReportGlobalConfigurationPaths()
  {
    TransferCsOptions options = new()
    {
      DownloadPasswordMaxAttempts = -1,
      DownloadPasswordAttemptWindowMinutes = 0,
      DownloadPasswordUnlockHours = 0
    };

    ValidateOptionsResult result = new DownloadPasswordOptionsValidator().Validate(null, options);

    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:DownloadPasswordMaxAttempts "));
    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:DownloadPasswordAttemptWindowMinutes "));
    Assert.Contains(result.Failures!, error => error.StartsWith("TransferCs:DownloadPasswordUnlockHours "));
  }
}
