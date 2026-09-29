using System.Text.Json.Serialization;

namespace TransferCs.Api.Models;

public sealed record SetPasswordRequest([property: JsonPropertyName("password")] string? Password);
