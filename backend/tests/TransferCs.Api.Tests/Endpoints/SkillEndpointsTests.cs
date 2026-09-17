using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Tests.Endpoints;

public sealed class SkillEndpointsTests
{
  [Theory]
  [InlineData(null, "transfer-cs")]
  [InlineData("company-transfers", "company-transfers")]
  [InlineData("123", "123")]
  [InlineData("true", "true")]
  [InlineData("null", "null")]
  public async Task Skill_UsesConfiguredNameAsQuotedTextAsync(string? configured, string expected)
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(options =>
    {
      if (configured != null)
        options.SkillName = configured;
    });
    using HttpClient client = factory.CreateClient();
    using HttpResponseMessage response = await client.GetAsync("/SKILL.md");

    response.EnsureSuccessStatusCode();
    Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
    Assert.Equal(expected, ReadSkillName(await response.Content.ReadAsStringAsync()));
  }

  [Fact]
  public async Task SiteNames_OverrideOrInheritWithoutLeakingAcrossRequestsAsync()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(options =>
    {
      options.SkillName = "company-transfers";
      options.InitialSiteId = "alpha";
      options.Sites = new()
      {
        ["alpha"] = new() { Hosts = ["alpha.test"], SkillName = "alpha-transfers" },
        ["beta"] = new() { Hosts = ["beta.test"] }
      };
    });
    using HttpClient client = factory.CreateClient();
    foreach ((string host, string expected) in new[]
      { ("alpha.test", "alpha-transfers"), ("beta.test", "company-transfers"), ("alpha.test", "alpha-transfers") })
    {
      using HttpRequestMessage request = new(HttpMethod.Get, "/SKILL.md");
      request.Headers.Host = host;
      using HttpResponseMessage response = await client.SendAsync(request);
      response.EnsureSuccessStatusCode();
      Assert.Equal(expected, ReadSkillName(await response.Content.ReadAsStringAsync()));
    }
  }

  [Theory]
  [InlineData(false, "TransferCs:SkillName")]
  [InlineData(true, "TransferCs:Sites:alpha:SkillName")]
  public void InvalidName_IsRejectedAtStartup(bool siteOverride, string path)
  {
    using WebApplicationFactory<Program> factory = CreateFactory(options =>
    {
      if (siteOverride)
      {
        options.InitialSiteId = "alpha";
        options.Sites = new() { ["alpha"] = new() { Hosts = ["alpha.test"], SkillName = "invalid name" } };
      }
      else
      {
        options.SkillName = "invalid name";
      }
    });
    OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    Assert.Contains(path, error.Message);
  }

  private static string? ReadSkillName(string content)
  {
    string[] lines = content.Split('\n');
    Assert.Equal("---", lines[0]);
    const string prefix = "name: ";
    Assert.StartsWith(prefix, lines[1]);
    // The quoted YAML scalar is also a JSON string, including numeric and boolean-like names.
    return JsonSerializer.Deserialize<string>(lines[1][prefix.Length..]);
  }

  private static WebApplicationFactory<Program> CreateFactory(Action<TransferCsOptions> configure) =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => services.Configure(configure)));
}
