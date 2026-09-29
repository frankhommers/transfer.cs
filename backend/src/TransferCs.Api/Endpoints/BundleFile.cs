using TransferCs.Api.Models;

namespace TransferCs.Api.Endpoints;

internal sealed record BundleFile(string Token, string Filename, FileMetadata Metadata);
