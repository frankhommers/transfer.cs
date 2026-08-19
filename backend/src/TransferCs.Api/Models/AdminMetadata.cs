namespace TransferCs.Api.Models;

public class AdminMetadata
{
  public string Filename { get; set; } = "";
  public long ContentLength { get; set; }
  public string ContentType { get; set; } = "";
  public string Sha256 { get; set; } = "";
  public int Downloads { get; set; }
  public int MaxDownloads { get; set; }
  public DateTime MaxDate { get; set; }
  public int DownloadLogTotal { get; set; }
  public List<DownloadEntry> DownloadLog { get; set; } = [];
}
