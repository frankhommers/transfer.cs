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
      options.SkillDescription = "Share files with the company.";
      options.InitialSiteId = "alpha";
      options.Sites = new()
      {
        ["alpha"] = new()
        {
          Hosts = ["alpha.test"],
          SkillName = "alpha-transfers",
          SkillDescription = "Share files with the Alpha team."
        },
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
      string content = await response.Content.ReadAsStringAsync();
      Assert.Equal(expected, ReadSkillName(content));
      string expectedDescription = host == "alpha.test"
        ? "Share files with the Alpha team." : "Share files with the company.";
      Assert.Contains($"\n  {expectedDescription}\n", content);
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

  [Theory]
  [InlineData(null)]
  [InlineData("Share confidential files with the project team.")]
  [InlineData("For \"Alpha\": privé # files 🚀")]
  [InlineData("Use {{Title}} and {{BaseUrl}} literally")]
  public async Task Skill_UsesConfiguredDescriptionOrDefaultAsync(string? configured)
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(options =>
    {
      if (configured != null)
        options.SkillDescription = configured;
    });
    using HttpClient client = factory.CreateClient();
    string content = await client.GetStringAsync("/SKILL.md");

    string expected = configured ?? new TransferCsOptions().SkillDescription;
    Assert.Contains($"\n  {expected}\n", content);
    Assert.DoesNotContain("{{SkillDescription}}", content);
  }

  [Theory]
  [InlineData(false, "TransferCs:SkillDescription")]
  [InlineData(true, "TransferCs:Sites:alpha:SkillDescription")]
  public void EmptyDescription_IsRejectedAtStartup(bool siteOverride, string path)
  {
    using WebApplicationFactory<Program> factory = CreateFactory(options =>
    {
      if (siteOverride)
      {
        options.InitialSiteId = "alpha";
        options.Sites = new() { ["alpha"] = new() { Hosts = ["alpha.test"], SkillDescription = "" } };
      }
      else
      {
        options.SkillDescription = "";
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
