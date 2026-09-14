namespace TransferCs.Api.Helpers;

public static class UploadLinkHelper
{
  public const string AdminRelation = "https://github.com/frankhommers/transfer.cs#file-administration";
  public const string DeleteRelation = "https://github.com/frankhommers/transfer.cs#file-deletion";

  public static string Format(string url, string relation) => $"<{url}>; rel=\"{relation}\"";
}
