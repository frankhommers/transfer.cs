using System.Text.Json.Serialization;

namespace TransferCs.Api.Models;

public class FileMetadata
{
  [JsonPropertyName("Generation")] public string Generation { get; set; } = "";

  [JsonPropertyName("ContentType")] public string ContentType { get; set; } = "";

  [JsonPropertyName("ContentLength")] public long ContentLength { get; set; }

  [JsonPropertyName("Downloads")] public int Downloads { get; set; }

  [JsonPropertyName("MaxDownloads")] public int MaxDownloads { get; set; } = -1;

  [JsonPropertyName("MaxDate")] public DateTime MaxDate { get; set; } = DateTime.MinValue;

  [JsonPropertyName("DeletionToken")] public string DeletionToken { get; set; } = "";

  [JsonPropertyName("AdminToken")] public string AdminToken { get; set; } = "";

  [JsonPropertyName("DownloadLogTotal")] public int DownloadLogTotal { get; set; }

  [JsonPropertyName("DownloadLog")] public List<DownloadEntry> DownloadLog { get; set; } = [];

  [JsonPropertyName("Encrypted")] public bool Encrypted { get; set; }

  /// <summary>
  /// SHA-256 of the file as it was received, in lowercase hex. For server-side encrypted
  /// uploads this is the digest of the plaintext, so it matches what the uploader computes
  /// locally. Empty for uploads made before this field existed.
  /// </summary>
  [JsonPropertyName("Sha256")] public string Sha256 { get; set; } = "";

  [JsonPropertyName("DecryptedContentType")]
  public string DecryptedContentType { get; set; } = "";

  public bool IsMaxDownloadsExpired => MaxDownloads != -1 && Downloads >= MaxDownloads;
  public bool IsMaxDateExpired => MaxDate != DateTime.MinValue && DateTime.UtcNow > MaxDate;

  public string RemainingDownloads =>
    MaxDownloads == -1 ? "n/a" : (MaxDownloads - Downloads).ToString();

  public string RemainingDays =>
    MaxDate == DateTime.MinValue ? "n/a" : ((int)(MaxDate - DateTime.UtcNow).TotalDays + 1).ToString();
}
