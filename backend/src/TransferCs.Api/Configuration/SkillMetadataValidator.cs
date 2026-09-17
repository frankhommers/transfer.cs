using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TransferCs.Api.Configuration;

public sealed partial class SkillMetadataValidator : IValidateOptions<TransferCsOptions>
{
  public ValidateOptionsResult Validate(string? name, TransferCsOptions options)
  {
    List<string> errors = [];
    ValidateName(options.SkillName, "TransferCs:SkillName", errors);
    ValidateDescription(options.SkillDescription, "TransferCs:SkillDescription", errors);
    foreach ((string siteId, SiteOptions site) in options.Sites)
    {
      if (site.SkillName != null)
        ValidateName(site.SkillName, $"TransferCs:Sites:{siteId}:SkillName", errors);
      if (site.SkillDescription != null)
        ValidateDescription(site.SkillDescription, $"TransferCs:Sites:{siteId}:SkillDescription", errors);
    }
    return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
  }

  private static void ValidateName(string? value, string path, List<string> errors)
  {
    if (value is not { Length: > 0 and <= 64 } || !NamePattern().IsMatch(value))
      errors.Add($"{path} must contain 1 to 64 lowercase letters, digits, or single separating hyphens.");
  }

  [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z")]
  private static partial Regex NamePattern();

  private static void ValidateDescription(string? value, string path, List<string> errors)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 ||
        value.Any(character => character is '<' or '>' or '\ufffe' or '\uffff' ||
          char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
      errors.Add($"{path} must contain 1 to 1024 characters of plain text without angle brackets " +
        "or control characters other than tabs and line breaks.");
  }
}
