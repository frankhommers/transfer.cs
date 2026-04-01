namespace TransferCs.Api.Models;

public class PreviewResult
{
  public string ContentType { get; set; } = "";
  public string Filename { get; set; } = "";
  public string Url { get; set; } = "";
  public string DownloadUrl { get; set; } = "";
  public string Token { get; set; } = "";
  public string Hostname { get; set; } = "";
  public long ContentLength { get; set; }
  public string QrCode { get; set; } = "";
  public string PreviewType { get; set; } = "";
}
