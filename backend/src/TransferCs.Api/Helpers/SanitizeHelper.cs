using System.Globalization;
using System.Text;

namespace TransferCs.Api.Helpers;

public static class SanitizeHelper
{
  public static string SanitizeFilename(string filename)
  {
    string normalized = filename.Normalize(NormalizationForm.FormC);

    StringBuilder sb = new(normalized.Length);
    foreach (char c in normalized)
    {
      UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
      if (category is UnicodeCategory.Control
          or UnicodeCategory.Format
          or UnicodeCategory.PrivateUse
          or UnicodeCategory.Surrogate)
        continue;

      sb.Append(c);
    }

    string result = Path.GetFileName(sb.ToString());
    return string.IsNullOrWhiteSpace(result) ? "_" : result;
  }
}