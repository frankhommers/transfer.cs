using System.Text.Json.Serialization;

namespace TransferCs.Api.Models;

public sealed record UnlockRequest([property: JsonPropertyName("password")] string? Password);
