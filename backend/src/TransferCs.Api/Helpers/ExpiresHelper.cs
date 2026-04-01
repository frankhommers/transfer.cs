using System.Globalization;
using System.Text.RegularExpressions;

namespace TransferCs.Api.Helpers;

public static partial class ExpiresHelper
{
  /// <summary>
  /// Parses an Expires header value into an absolute UTC DateTime.
  /// 
  /// Accepts:
  ///   - Duration: "7d", "12h", "30m", "90s", "7d12h", "1d6h30m", "3600s"
  ///   - HTTP date: "Thu, 15 Apr 2026 07:38:01 GMT" (RFC 7231)
  ///   - ISO 8601: "2026-04-15T07:38:01Z"
  /// 
  /// Returns null if the value cannot be parsed or results in a past date.
  /// </summary>
  public static DateTime? Parse(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return null;

    value = value.Trim();

    // Try duration format: 7d12h30m90s (any combination)
    Match match = DurationRegex().Match(value);
    if (match.Success && match.Length == value.Length)
    {
      TimeSpan duration = TimeSpan.Zero;

      if (match.Groups["days"].Success)
        duration += TimeSpan.FromDays(int.Parse(match.Groups["days"].Value));
      if (match.Groups["hours"].Success)
        duration += TimeSpan.FromHours(int.Parse(match.Groups["hours"].Value));
      if (match.Groups["minutes"].Success)
        duration += TimeSpan.FromMinutes(int.Parse(match.Groups["minutes"].Value));
      if (match.Groups["seconds"].Success)
        duration += TimeSpan.FromSeconds(int.Parse(match.Groups["seconds"].Value));

      if (duration <= TimeSpan.Zero)
        return null;

      return DateTime.UtcNow + duration;
    }

    // Try HTTP date (RFC 7231): "Thu, 15 Apr 2026 07:38:01 GMT"
    if (DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture, DateTimeStyles.None,
          out DateTimeOffset httpDate)) return httpDate.UtcDateTime > DateTime.UtcNow ? httpDate.UtcDateTime : null;

    // Try ISO 8601: "2026-04-15T07:38:01Z"
    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset isoDate))
      return isoDate.UtcDateTime > DateTime.UtcNow ? isoDate.UtcDateTime : null;

    return null;
  }

  /// <summary>
  /// Formats a UTC DateTime as an RFC 7231 HTTP date for the Expires response header.
  /// </summary>
  public static string FormatHttpDate(DateTime utcDate)
  {
    return utcDate.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture);
  }

  [GeneratedRegex(@"^(?:(?<days>\d+)d)?(?:(?<hours>\d+)h)?(?:(?<minutes>\d+)m)?(?:(?<seconds>\d+)s)?$",
    RegexOptions.IgnoreCase)]
  private static partial Regex DurationRegex();
}