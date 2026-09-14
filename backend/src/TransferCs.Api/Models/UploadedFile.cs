using System.Text.Json.Serialization;

namespace TransferCs.Api.Models;

public sealed record UploadedFile(
  [property: JsonPropertyName("filename")] string Filename,
  [property: JsonPropertyName("url")] string Url,
  [property: JsonPropertyName("deleteUrl")] string DeleteUrl,
  [property: JsonPropertyName("adminUrl")] string AdminUrl,
  [property: JsonPropertyName("sha256")] string Sha256,
  [property: JsonPropertyName("expires")] DateTime? Expires);
