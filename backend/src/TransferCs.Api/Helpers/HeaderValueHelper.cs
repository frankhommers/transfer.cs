using System.Text;

namespace TransferCs.Api.Helpers;

public static class HeaderValueHelper
{
  public static string ToSafeValue(string value)
  {
    StringBuilder builder = new(value.Length);
    foreach (char character in value.Normalize(NormalizationForm.FormD))
      if (character is > ' ' and <= '~' || character == ' ' && builder.Length > 0 && builder[^1] != ' ')
        builder.Append(character);
    return builder.ToString().TrimEnd();
  }

  public static string ToQuotedString(string value) =>
    $"\"{ToSafeValue(value).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
