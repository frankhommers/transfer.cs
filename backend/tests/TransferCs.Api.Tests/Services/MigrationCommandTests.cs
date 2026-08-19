using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;

namespace TransferCs.Api.Tests.Services;

public class MigrationCommandTests : IDisposable
{
  private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"transfer-command-{Guid.NewGuid():N}");

  [Theory]
  [InlineData(new[] { "migrate-legacy-data" }, true)]
  [InlineData(new string[0], false)]
  [InlineData(new[] { "migrate-legacy-data", "extra" }, false)]
  [InlineData(new[] { "MIGRATE-LEGACY-DATA" }, false)]
  public void IsRequested_RequiresExactSingleArgument(string[] args, bool expected)
  {
    Assert.Equal(expected, MigrationCommand.IsRequested(args));
  }

  [Fact]
  public void Execute_MigratesAndReportsDirectoryCount()
  {
    Directory.CreateDirectory(Path.Combine(_basePath, "first"));
    Directory.CreateDirectory(Path.Combine(_basePath, "second"));
    TransferCsOptions options = new()
    {
      BasePath = _basePath,
      InitialSiteId = "alpha",
      Sites = new Dictionary<string, SiteOptions>
      {
        ["alpha"] = new() { Hosts = ["alpha.test"] }
      }
    };
    SiteResolver resolver = new(Options.Create(options));
    SiteDataMigration migration = new(Options.Create(options), resolver);
    ServiceProvider services = new ServiceCollection()
      .AddSingleton(migration)
      .AddSingleton(resolver)
      .BuildServiceProvider();
    using StringWriter output = new();

    MigrationCommand.Execute(services, output);

    Assert.Equal("Migrated 2 legacy token directories to site 'alpha'.", output.ToString().Trim());
  }

  public void Dispose()
  {
    if (Directory.Exists(_basePath))
      Directory.Delete(_basePath, true);
  }
}
