using TransferCs.Api.Configuration;

namespace TransferCs.Api.Services;

public sealed record ResolvedSite(
  string Id,
  IReadOnlyList<string> Hosts,
  string DataDirectory,
  TransferCsOptions Options);
