namespace TransferCs.Api.Models;

public class PublicConfig
{
  public string Title { get; set; } = "transfer.cs";
  public int PurgeDays { get; set; }
  public long MaxUploadSizeKb { get; set; }
}
