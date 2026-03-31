using System.Globalization;
using System.Text;

namespace TransferCs.Api.Helpers;

public static class SanitizeHelper
{
    public static string SanitizeFilename(string filename)
    {
        var normalized = filename.Normalize(NormalizationForm.FormC);

        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.Surrogate)
            {
                continue;
            }

            sb.Append(c);
        }

        var result = Path.GetFileName(sb.ToString());
        return string.IsNullOrWhiteSpace(result) ? "_" : result;
    }
}
