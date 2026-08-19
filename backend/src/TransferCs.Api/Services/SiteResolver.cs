using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Services;

public sealed partial class SiteResolver
{
  private readonly Dictionary<string, ResolvedSite> _sitesByHost = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, ResolvedSite> _sitesById = new(StringComparer.Ordinal);

  public SiteResolver(IOptions<TransferCsOptions> optionsAccessor)
  {
    TransferCsOptions options = optionsAccessor.Value;
    LegacySite = new ResolvedSite("default", [], "default", options);
    if (options.Sites.Count == 0)
    {
      InitialSite = LegacySite;
      return;
    }

    HashSet<string> dataDirectories = new(StringComparer.OrdinalIgnoreCase);
    foreach ((string siteId, SiteOptions siteOptions) in options.Sites)
    {
      ValidateSiteId(siteId);
      if (siteOptions.Hosts.Count == 0)
        throw new InvalidOperationException($"Site '{siteId}' must configure at least one host.");

      string dataDirectory = siteOptions.DataDirectory ?? siteId;
      ValidateDataDirectory(siteId, dataDirectory);
      if (!dataDirectories.Add(dataDirectory))
        throw new InvalidOperationException($"DataDirectory '{dataDirectory}' is used by more than one site.");
      TransferCsOptions effectiveOptions = CreateEffectiveOptions(options, siteOptions);
      if (effectiveOptions.RandomTokenLength is < 6 or > 128)
        throw new InvalidOperationException($"Site '{siteId}' RandomTokenLength must be between 6 and 128.");

      List<string> hosts = siteOptions.Hosts.Select(NormalizeHost).ToList();
      ResolvedSite site = new(siteId, hosts, dataDirectory, effectiveOptions);
      _sitesById.Add(siteId, site);
      foreach (string host in hosts)
        if (!_sitesByHost.TryAdd(host, site))
          throw new InvalidOperationException($"Host '{host}' is configured for more than one site.");
    }

    if (string.IsNullOrWhiteSpace(options.InitialSiteId) ||
        !_sitesById.TryGetValue(options.InitialSiteId, out ResolvedSite? initialSite))
      throw new InvalidOperationException("InitialSiteId must identify one configured site.");
    InitialSite = initialSite;
  }

  public ResolvedSite LegacySite { get; }

  public ResolvedSite InitialSite { get; }

  public bool IsMultiSite => _sitesByHost.Count > 0;

  public IReadOnlyCollection<ResolvedSite> Sites => _sitesByHost.Values.Distinct().ToArray();

  public ResolvedSite? Resolve(string host)
  {
    if (!IsMultiSite)
      return LegacySite;
    return _sitesByHost.GetValueOrDefault(NormalizeHost(host));
  }

  private static string NormalizeHost(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();

  private static void ValidateSiteId(string siteId)
  {
    if (!SiteIdPattern().IsMatch(siteId))
      throw new InvalidOperationException(
        $"Invalid site ID '{siteId}'. Use lowercase letters, numbers, and hyphens.");
  }

  private static void ValidateDataDirectory(string siteId, string dataDirectory)
  {
    if (string.IsNullOrWhiteSpace(dataDirectory) || dataDirectory is "." or ".." ||
        !string.Equals(Path.GetFileName(dataDirectory), dataDirectory, StringComparison.Ordinal))
      throw new InvalidOperationException($"Site '{siteId}' has an invalid DataDirectory.");
  }

  private static TransferCsOptions CreateEffectiveOptions(TransferCsOptions global, SiteOptions site) => new()
  {
    Title = site.Title ?? global.Title,
    Provider = global.Provider,
    BasePath = global.BasePath,
    TempPath = global.TempPath,
    MaxUploadSizeKb = site.MaxUploadSizeKb ?? global.MaxUploadSizeKb,
    PurgeDays = site.PurgeDays ?? global.PurgeDays,
    PurgeIntervalHours = global.PurgeIntervalHours,
    RateLimitRequestsPerMinute = global.RateLimitRequestsPerMinute,
    RandomTokenLength = site.RandomTokenLength ?? global.RandomTokenLength,
    DownloadLogEnabled = global.DownloadLogEnabled,
    DownloadLogMaxEntries = global.DownloadLogMaxEntries,
    ForceHttps = global.ForceHttps,
    EmailContact = global.EmailContact,
    ClamAvHost = global.ClamAvHost,
    PerformClamAvPrescan = global.PerformClamAvPrescan,
    VirusTotalKey = global.VirusTotalKey,
    HttpAuthUser = global.HttpAuthUser,
    HttpAuthPass = global.HttpAuthPass,
    HttpAuthHtpasswd = global.HttpAuthHtpasswd,
    HttpAuthIpWhitelist = global.HttpAuthIpWhitelist,
    IpWhitelist = global.IpWhitelist,
    IpBlacklist = global.IpBlacklist,
    CorsDomains = global.CorsDomains,
    BaseUrl = site.BaseUrl ?? global.BaseUrl,
    ProxyPath = global.ProxyPath,
    ProxyPort = global.ProxyPort,
    TrustedProxies = global.TrustedProxies,
    InitialSiteId = global.InitialSiteId,
    Sites = global.Sites
  };

  [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
  private static partial Regex SiteIdPattern();
}
