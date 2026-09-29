namespace TransferCs.Api.Models;

public class LockedPreviewResult
{
  public string Filename { get; set; } = "";
  public string Url { get; set; } = "";
  public string DownloadUrl { get; set; } = "";
  public string Token { get; set; } = "";
  public string Hostname { get; set; } = "";
  public string QrCode { get; set; } = "";
  public bool PasswordProtected { get; set; } = true;
}
