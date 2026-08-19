namespace TransferCs.Api.Configuration;

public static class UploadLimitHelper
{
  public static long? ResolveKestrelLimit(TransferCsOptions options)
  {
    IEnumerable<long> limits = options.Sites.Count == 0
      ? [options.MaxUploadSizeKb]
      : options.Sites.Values.Select(site => site.MaxUploadSizeKb ?? options.MaxUploadSizeKb);
    long[] configuredLimits = limits.ToArray();
    if (configuredLimits.Any(limit => limit <= 0))
      return null;
    return configuredLimits.Max() * 1024;
  }
}
