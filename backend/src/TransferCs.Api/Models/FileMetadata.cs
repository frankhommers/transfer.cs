using System.Text.Json.Serialization;

namespace TransferCs.Api.Models;

public class FileMetadata
{
  [JsonPropertyName("ContentType")] public string ContentType { get; set; } = "";

  [JsonPropertyName("ContentLength")] public long ContentLength { get; set; }

  [JsonPropertyName("Downloads")] public int Downloads { get; set; }

  [JsonPropertyName("MaxDownloads")] public int MaxDownloads { get; set; } = -1;

  [JsonPropertyName("MaxDate")] public DateTime MaxDate { get; set; } = DateTime.MinValue;

  [JsonPropertyName("DeletionToken")] public string DeletionToken { get; set; } = "";

  [JsonPropertyName("Encrypted")] public bool Encrypted { get; set; }

  [JsonPropertyName("DecryptedContentType")]
  public string DecryptedContentType { get; set; } = "";

  public bool IsMaxDownloadsExpired => MaxDownloads != -1 && Downloads >= MaxDownloads;
  public bool IsMaxDateExpired => MaxDate != DateTime.MinValue && DateTime.UtcNow > MaxDate;

  public string RemainingDownloads =>
    MaxDownloads == -1 ? "n/a" : (MaxDownloads - Downloads).ToString();

  public string RemainingDays =>
    MaxDate == DateTime.MinValue ? "n/a" : ((int)(MaxDate - DateTime.UtcNow).TotalDays + 1).ToString();
}