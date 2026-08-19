using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;

namespace TransferCs.Api.Storage;

public sealed class SiteStorageFactory
{
  private readonly string _basePath;
  private readonly string _canonicalBasePath;
  private readonly SiteResolver _resolver;
  private readonly ConcurrentDictionary<string, IStorageProvider> _providers = new(StringComparer.Ordinal);

  public SiteStorageFactory(IOptions<TransferCsOptions> optionsAccessor, SiteResolver resolver)
  {
    _basePath = optionsAccessor.Value.BasePath;
    _canonicalBasePath = Path.GetFullPath(_basePath);
    _resolver = resolver;
  }

  public string Type => "local";

  public IStorageProvider Get(ResolvedSite site) =>
    _providers.GetOrAdd(site.Id, _ => new LocalStorageProvider(ResolvePath(site)));

  private string ResolvePath(ResolvedSite site)
  {
    if (!_resolver.IsMultiSite)
      return _canonicalBasePath;

    string path = Path.GetFullPath(Path.Combine(_canonicalBasePath, site.DataDirectory));
    string prefix = _canonicalBasePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!path.StartsWith(prefix, StringComparison.Ordinal))
      throw new InvalidOperationException($"Site '{site.Id}' resolves outside BasePath.");
    if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
      throw new InvalidOperationException($"Site '{site.Id}' DataDirectory cannot be a symbolic link.");
    return path;
  }
}
