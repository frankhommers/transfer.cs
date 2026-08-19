using TransferCs.Api.Configuration;
using TransferCs.Api.Services;
using Microsoft.Extensions.Options;

namespace TransferCs.Api.Tests.Services;

public class SiteResolverTests
{
  [Fact]
  public void Resolve_MatchesExactHostCaseInsensitively()
  {
    TransferCsOptions options = CreateOptions();
    SiteResolver resolver = new(Options.Create(options));

    ResolvedSite? site = resolver.Resolve("FILES.EXAMPLE.TEST");

    Assert.NotNull(site);
    Assert.Equal("alpha", site.Id);
    Assert.Equal("Alpha", site.Options.Title);
    Assert.Equal("alpha-data", site.DataDirectory);
  }

  [Fact]
  public void Resolve_UnknownHostReturnsNullWhenSitesAreConfigured()
  {
    SiteResolver resolver = new(Options.Create(CreateOptions()));

    Assert.Null(resolver.Resolve("unknown.example.test"));
  }

  [Fact]
  public void Constructor_RejectsDuplicateHosts()
  {
    TransferCsOptions options = CreateOptions();
    options.Sites["beta"] = new SiteOptions { Hosts = ["files.example.test"] };

    Assert.Throws<InvalidOperationException>(() => new SiteResolver(Options.Create(options)));
  }

  [Fact]
  public void Constructor_RejectsDataDirectoriesThatCollideByCase()
  {
    TransferCsOptions options = CreateOptions();
    options.Sites["beta"] = new SiteOptions
    {
      Hosts = ["beta.test"],
      DataDirectory = "ALPHA-DATA"
    };

    Assert.Throws<InvalidOperationException>(() => new SiteResolver(Options.Create(options)));
  }

  [Fact]
  public void Resolve_UsesLegacySiteWhenNoSitesAreConfigured()
  {
    TransferCsOptions options = new() { Title = "Legacy" };
    SiteResolver resolver = new(Options.Create(options));

    ResolvedSite? site = resolver.Resolve("anything.example.test");

    Assert.NotNull(site);
    Assert.Equal("default", site.Id);
    Assert.Equal("Legacy", site.Options.Title);
  }

  private static TransferCsOptions CreateOptions() => new()
  {
    InitialSiteId = "alpha",
    Sites = new Dictionary<string, SiteOptions>
    {
      ["alpha"] = new()
      {
        Hosts = ["files.example.test"],
        Title = "Alpha",
        DataDirectory = "alpha-data"
      }
    }
  };
}
