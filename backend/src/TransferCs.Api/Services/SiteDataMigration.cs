using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Services;

public sealed class SiteDataMigration
{
  private const string MarkerFilename = ".multisite-migration-v1";
  private readonly string _basePath;
  private readonly SiteResolver _resolver;

  public SiteDataMigration(IOptions<TransferCsOptions> optionsAccessor, SiteResolver resolver)
  {
    _basePath = optionsAccessor.Value.BasePath;
    _resolver = resolver;
  }

  public void Run()
  {
    if (!_resolver.IsMultiSite)
      return;

    Directory.CreateDirectory(_basePath);
    string markerPath = Path.Combine(_basePath, MarkerFilename);
    if (File.Exists(markerPath))
      return;

    HashSet<string> siteDirectories = _resolver.Sites
      .Select(site => site.DataDirectory)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (string siteDirectory in siteDirectories)
    {
      string path = Path.Combine(_basePath, siteDirectory);
      if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidOperationException($"Configured site directory '{path}' cannot be a symbolic link.");
      if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        throw new InvalidOperationException(
          $"Cannot determine whether '{path}' is legacy data or an initialized site directory.");
    }

    List<string> legacyDirectories = Directory.GetDirectories(_basePath)
      .Where(path => !siteDirectories.Contains(Path.GetFileName(path)))
      .ToList();

    string targetRoot = Path.Combine(_basePath, _resolver.InitialSite.DataDirectory);
    foreach (string source in legacyDirectories)
    {
      string destination = Path.Combine(targetRoot, Path.GetFileName(source));
      if (Directory.Exists(destination))
        throw new InvalidOperationException(
          $"Cannot migrate legacy data because '{destination}' already exists.");
    }

    Directory.CreateDirectory(targetRoot);
    foreach (string source in legacyDirectories)
      Directory.Move(source, Path.Combine(targetRoot, Path.GetFileName(source)));

    File.WriteAllText(markerPath, _resolver.InitialSite.Id);
  }
}
