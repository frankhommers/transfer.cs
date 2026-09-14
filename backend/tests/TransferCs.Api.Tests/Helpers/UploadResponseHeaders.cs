namespace TransferCs.Api.Tests.Helpers;

internal static class UploadResponseHeaders
{
  public static string AdminUrl(HttpResponseMessage response) =>
    FindLink(response, "https://github.com/frankhommers/transfer.cs#file-administration");

  public static string DeleteUrl(HttpResponseMessage response) =>
    FindLink(response, "https://github.com/frankhommers/transfer.cs#file-deletion");

  private static string FindLink(HttpResponseMessage response, string relation)
  {
    string link = Assert.Single(response.Headers.GetValues("Link"),
      value => value.Contains($"rel=\"{relation}\"", StringComparison.Ordinal));
    Assert.StartsWith("<", link);
    return link[1..link.IndexOf('>')];
  }
}
