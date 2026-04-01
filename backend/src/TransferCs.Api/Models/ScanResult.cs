namespace TransferCs.Api.Models;

public class ScanResult
{
  public string Filename { get; set; } = "";
  public bool IsClean { get; set; }
  public string Status { get; set; } = "";
}
