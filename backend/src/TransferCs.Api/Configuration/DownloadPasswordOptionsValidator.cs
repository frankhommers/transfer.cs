using Microsoft.Extensions.Options;

namespace TransferCs.Api.Configuration;

public sealed class DownloadPasswordOptionsValidator : IValidateOptions<TransferCsOptions>
{
  private const int MaxWindowMinutes = 525_600;
  private const int MaxUnlockHours = 87_600;

  public ValidateOptionsResult Validate(string? name, TransferCsOptions options)
  {
    List<string> errors = [];
    ValidateValues(options.DownloadPasswordMaxAttempts, options.DownloadPasswordAttemptWindowMinutes,
      options.DownloadPasswordUnlockHours, "TransferCs", errors);
    foreach ((string siteId, SiteOptions site) in options.Sites)
      ValidateValues(site.DownloadPasswordMaxAttempts, site.DownloadPasswordAttemptWindowMinutes,
        site.DownloadPasswordUnlockHours, $"TransferCs:Sites:{siteId}", errors);
    return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
  }

  private static void ValidateValues(int? maxAttempts, int? windowMinutes, int? unlockHours, string path,
    List<string> errors)
  {
    if (maxAttempts is < 0)
      errors.Add($"{path}:DownloadPasswordMaxAttempts must be 0 (unlimited) or greater.");
    if (windowMinutes is < 1 or > MaxWindowMinutes)
      errors.Add($"{path}:DownloadPasswordAttemptWindowMinutes must be between 1 and {MaxWindowMinutes}.");
    if (unlockHours is < 1 or > MaxUnlockHours)
      errors.Add($"{path}:DownloadPasswordUnlockHours must be between 1 and {MaxUnlockHours}.");
  }
}
