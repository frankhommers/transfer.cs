namespace TransferCs.Api.Configuration;

public class TransferCsOptions
{
  public const string SectionName = "TransferCs";

  public string Title { get; set; } = "transfer.cs";
  public string Provider { get; set; } = "local";
  public string BasePath { get; set; } = "./data";
  public string TempPath { get; set; } = Path.GetTempPath();
  public long MaxUploadSizeKb { get; set; }
  public int PurgeDays { get; set; }
  public int PurgeIntervalHours { get; set; }
  public int RateLimitRequestsPerMinute { get; set; }
  public int RandomTokenLength { get; set; } = 10;
  public bool ForceHttps { get; set; }
  public string EmailContact { get; set; } = "";
  public string ClamAvHost { get; set; } = "";
  public bool PerformClamAvPrescan { get; set; }
  public string VirusTotalKey { get; set; } = "";
  public string HttpAuthUser { get; set; } = "";
  public string HttpAuthPass { get; set; } = "";
  public string HttpAuthHtpasswd { get; set; } = "";
  public string HttpAuthIpWhitelist { get; set; } = "";
  public string IpWhitelist { get; set; } = "";
  public string IpBlacklist { get; set; } = "";
  public string CorsDomains { get; set; } = "";
  public string BaseUrl { get; set; } = "";
  public string ProxyPath { get; set; } = "";
  public string ProxyPort { get; set; } = "";

  public long MaxUploadSizeBytes => MaxUploadSizeKb * 1024;
}