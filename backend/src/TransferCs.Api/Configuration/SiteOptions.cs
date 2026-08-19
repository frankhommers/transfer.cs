namespace TransferCs.Api.Configuration;

public class SiteOptions
{
  public List<string> Hosts { get; set; } = [];
  public string? Title { get; set; }
  public string? BaseUrl { get; set; }
  public string? DataDirectory { get; set; }
  public int? PurgeDays { get; set; }
  public long? MaxUploadSizeKb { get; set; }
  public int? RandomTokenLength { get; set; }
}
