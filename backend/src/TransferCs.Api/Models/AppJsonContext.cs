using System.Text.Json.Serialization;

namespace TransferCs.Api.Models;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FileMetadata))]
[JsonSerializable(typeof(ScanResult))]
[JsonSerializable(typeof(VirusTotalResult))]
[JsonSerializable(typeof(PreviewResult))]
[JsonSerializable(typeof(PublicConfig))]
[JsonSerializable(typeof(HealthResponse))]
public partial class AppJsonContext : JsonSerializerContext;
