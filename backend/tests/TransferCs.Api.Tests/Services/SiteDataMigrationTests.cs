using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;

namespace TransferCs.Api.Tests.Services;

public class SiteDataMigrationTests : IDisposable
{
  private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"transfer-migration-{Guid.NewGuid():N}");

  [Fact]
  public void Run_MovesLegacyTokenDirectoriesToInitialSite()
  {
    string legacyDirectory = Path.Combine(_basePath, "legacy-token");
    Directory.CreateDirectory(legacyDirectory);
    File.WriteAllText(Path.Combine(legacyDirectory, "file.txt"), "legacy");
    SiteDataMigration migration = CreateMigration();

    int migratedDirectories = migration.Run();

    Assert.Equal(1, migratedDirectories);
    Assert.False(Directory.Exists(legacyDirectory));
    Assert.Equal("legacy", File.ReadAllText(Path.Combine(_basePath, "alpha", "legacy-token", "file.txt")));
    Assert.False(File.Exists(Path.Combine(_basePath, ".multisite-migration-v1")));
  }

  [Fact]
  public void Run_RefusesToMergeCollision()
  {
    Directory.CreateDirectory(Path.Combine(_basePath, "legacy-token"));
    Directory.CreateDirectory(Path.Combine(_basePath, "alpha", "legacy-token"));
    SiteDataMigration migration = CreateMigration();

    Assert.Throws<InvalidOperationException>(() => migration.Run());
  }

  [Fact]
  public void Run_RefusesAmbiguousConfiguredSiteDirectory()
  {
    string betaDirectory = Path.Combine(_basePath, "beta");
    Directory.CreateDirectory(betaDirectory);
    File.WriteAllText(Path.Combine(betaDirectory, "legacy-file.txt"), "ambiguous");

    Assert.Throws<InvalidOperationException>(() => CreateMigration().Run());
  }

  [Fact]
  public void Run_RefusesConfiguredSiteDirectorySymlink()
  {
    string outsidePath = Path.Combine(Path.GetTempPath(), $"transfer-migration-outside-{Guid.NewGuid():N}");
    Directory.CreateDirectory(outsidePath);
    Directory.CreateDirectory(_basePath);
    Directory.CreateSymbolicLink(Path.Combine(_basePath, "alpha"), outsidePath);

    try
    {
      Assert.Throws<InvalidOperationException>(() => CreateMigration().Run());
    }
    finally
    {
      Directory.Delete(outsidePath);
    }
  }

  [Fact]
  public void Run_RefusesSymlinkInsideLegacySource()
  {
    string outsidePath = Path.Combine(Path.GetTempPath(), $"transfer-legacy-outside-{Guid.NewGuid():N}");
    Directory.CreateDirectory(outsidePath);
    string legacyDirectory = Path.Combine(_basePath, "legacy-token");
    Directory.CreateDirectory(legacyDirectory);
    Directory.CreateSymbolicLink(Path.Combine(legacyDirectory, "linked"), outsidePath);

    try
    {
      Assert.Throws<InvalidOperationException>(() => CreateMigration().Run());
    }
    finally
    {
      Directory.Delete(outsidePath);
    }
  }

  [Fact]
  public void Run_RefusesConfiguredDirectoryWithDifferentCasing()
  {
    Directory.CreateDirectory(Path.Combine(_basePath, "alpha"));
    TransferCsOptions options = new()
    {
      BasePath = _basePath,
      InitialSiteId = "primary",
      Sites = new Dictionary<string, SiteOptions>
      {
        ["primary"] = new() { Hosts = ["alpha.test"], DataDirectory = "ALPHA" }
      }
    };
    SiteResolver resolver = new(Options.Create(options));
    SiteDataMigration migration = new(Options.Create(options), resolver);

    Assert.Throws<InvalidOperationException>(() => migration.Run());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Run_RefusesConfiguredPathOccupiedByNonDirectory(bool symbolicLink)
  {
    Directory.CreateDirectory(_basePath);
    string configuredPath = Path.Combine(_basePath, "alpha");
    if (symbolicLink)
      File.CreateSymbolicLink(configuredPath, Path.Combine(_basePath, "missing-target"));
    else
      File.WriteAllText(configuredPath, "not a directory");

    Assert.Throws<InvalidOperationException>(() => CreateMigration().Run());
  }

  [Fact]
  public void Run_RefusesSecondExecutionAfterMigration()
  {
    Directory.CreateDirectory(Path.Combine(_basePath, "legacy-token"));
    CreateMigration().Run();

    Assert.Throws<InvalidOperationException>(() => CreateMigration().Run());
  }

  [Fact]
  public void Run_WithoutMultiSiteConfigurationThrows()
  {
    TransferCsOptions options = new() { BasePath = _basePath };
    SiteResolver resolver = new(Options.Create(options));
    SiteDataMigration migration = new(Options.Create(options), resolver);

    Assert.Throws<InvalidOperationException>(() => migration.Run());
  }

  public void Dispose()
  {
    if (Directory.Exists(_basePath))
      Directory.Delete(_basePath, true);
  }

  private SiteDataMigration CreateMigration()
  {
    TransferCsOptions options = new()
    {
      BasePath = _basePath,
      InitialSiteId = "alpha",
      Sites = new Dictionary<string, SiteOptions>
      {
        ["alpha"] = new() { Hosts = ["alpha.test"] },
        ["beta"] = new() { Hosts = ["beta.test"] }
      }
    };
    SiteResolver resolver = new(Options.Create(options));
    return new SiteDataMigration(Options.Create(options), resolver);
  }
}
