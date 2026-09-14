using System.Text.Json.Serialization;

namespace TransferCs.Api.Models;

public sealed record UploadResponse(
  [property: JsonPropertyName("files")] IReadOnlyList<UploadedFile> Files);
