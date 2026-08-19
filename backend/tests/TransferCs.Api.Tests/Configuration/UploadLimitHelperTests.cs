using TransferCs.Api.Configuration;

namespace TransferCs.Api.Tests.Configuration;

public class UploadLimitHelperTests
{
  [Fact]
  public void ResolveKestrelLimit_UsesLargestConfiguredSiteLimit()
  {
    TransferCsOptions options = new()
    {
      MaxUploadSizeKb = 100,
      Sites = new Dictionary<string, SiteOptions>
      {
        ["small"] = new() { MaxUploadSizeKb = 50 },
        ["large"] = new() { MaxUploadSizeKb = 200 }
      }
    };

    Assert.Equal(200 * 1024, UploadLimitHelper.ResolveKestrelLimit(options));
  }

  [Fact]
  public void ResolveKestrelLimit_UsesUnlimitedWhenAnySiteIsUnlimited()
  {
    TransferCsOptions options = new()
    {
      MaxUploadSizeKb = 100,
      Sites = new Dictionary<string, SiteOptions>
      {
        ["limited"] = new() { MaxUploadSizeKb = 50 },
        ["unlimited"] = new() { MaxUploadSizeKb = 0 }
      }
    };

    Assert.Null(UploadLimitHelper.ResolveKestrelLimit(options));
  }
}
