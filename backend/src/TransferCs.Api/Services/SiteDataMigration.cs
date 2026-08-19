using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Services;

public sealed class SiteDataMigration
{
  private readonly string _basePath;
  private readonly SiteResolver _resolver;

  public SiteDataMigration(IOptions<TransferCsOptions> optionsAccessor, SiteResolver resolver)
  {
    _basePath = optionsAccessor.Value.BasePath;
    _resolver = resolver;
  }

  public int Run()
  {
    if (!_resolver.IsMultiSite)
      throw new InvalidOperationException("Legacy migration requires multi-site configuration.");

    Directory.CreateDirectory(_basePath);
    string[] rootEntries = Directory.GetFileSystemEntries(_basePath);
    HashSet<string> siteDirectories = _resolver.Sites
      .Select(site => site.DataDirectory)
      .ToHashSet(StringComparer.Ordinal);
    foreach (string siteDirectory in siteDirectories)
    {
      string[] matches = rootEntries.Where(path =>
        string.Equals(Path.GetFileName(path), siteDirectory, StringComparison.OrdinalIgnoreCase)).ToArray();
      foreach (string mismatch in matches.Where(path =>
                 !string.Equals(Path.GetFileName(path), siteDirectory, StringComparison.Ordinal)))
        throw new InvalidOperationException(
          $"Configured site directory '{siteDirectory}' conflicts with '{Path.GetFileName(mismatch)}'.");

      string? path = matches.SingleOrDefault();
      if (path == null)
        continue;

      FileAttributes attributes = File.GetAttributes(path);
      if ((attributes & FileAttributes.ReparsePoint) != 0)
        throw new InvalidOperationException($"Configured site directory '{path}' cannot be a symbolic link.");
      if ((attributes & FileAttributes.Directory) == 0)
        throw new InvalidOperationException($"Configured site directory '{path}' is not a directory.");
      if (Directory.EnumerateFileSystemEntries(path).Any())
        throw new InvalidOperationException(
          $"Cannot determine whether '{path}' is legacy data or an initialized site directory.");
    }

    List<string> legacyDirectories = rootEntries
      .Where(path => !siteDirectories.Contains(Path.GetFileName(path)))
      .Where(path => (File.GetAttributes(path) & FileAttributes.Directory) != 0)
      .ToList();
    foreach (string source in legacyDirectories)
      EnsureNoReparsePoints(source);

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

    return legacyDirectories.Count;
  }

  private static void EnsureNoReparsePoints(string path)
  {
    FileAttributes attributes = File.GetAttributes(path);
    if ((attributes & FileAttributes.ReparsePoint) != 0)
      throw new InvalidOperationException($"Legacy migration source '{path}' cannot contain symbolic links.");
    if ((attributes & FileAttributes.Directory) == 0)
      return;

    foreach (string entry in Directory.EnumerateFileSystemEntries(path))
      EnsureNoReparsePoints(entry);
  }
}
