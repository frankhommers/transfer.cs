using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;

namespace TransferCs.Api.Tests.Middleware;

public class ForwardedHeadersTests
{
  [Fact]
  public async Task TrustedProxy_UsesForwardedAddressForBlacklist()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:TrustedProxies"] = "*",
      ["TransferCs:IpBlacklist"] = "198.51.100.42"
    });
    using HttpClient client = factory.CreateClient();
    TransferCsOptions options = factory.Services.GetRequiredService<IOptions<TransferCsOptions>>().Value;
    using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "/health", "198.51.100.42");

    using HttpResponseMessage response = await client.SendAsync(request);

    Assert.Equal("*", options.TrustedProxies);
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task IpWhitelist_AcceptsCidrRange()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:TrustedProxies"] = "*",
      ["TransferCs:IpWhitelist"] = "198.51.100.0/24"
    });
    using HttpClient client = factory.CreateClient();
    using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "/health", "198.51.100.42");

    using HttpResponseMessage response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task UntrustedSource_CannotSetForwardedAddress()
  {
    Microsoft.AspNetCore.Builder.ForwardedHeadersOptions options = new();
    ForwardedHeadersSetup.Configure(options, "203.0.113.10");
    DefaultHttpContext context = new();
    context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.11");
    context.Request.Headers["X-Forwarded-For"] = "198.51.100.42";
    IPAddress? resolvedAddress = null;
    ForwardedHeadersMiddleware middleware = new(
      next: resolvedContext =>
      {
        resolvedAddress = resolvedContext.Connection.RemoteIpAddress;
        return Task.CompletedTask;
      },
      loggerFactory: NullLoggerFactory.Instance,
      options: Options.Create(options));

    await middleware.Invoke(context);

    Assert.Equal(IPAddress.Parse("203.0.113.11"), resolvedAddress);
  }

  [Fact]
  public async Task RateLimit_IsPartitionedByForwardedAddress()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:TrustedProxies"] = "*",
      ["TransferCs:RateLimitRequestsPerMinute"] = "1"
    });
    using HttpClient client = factory.CreateClient();
    using HttpRequestMessage first = CreateRequest(HttpMethod.Get, "/health", "198.51.100.1");
    using HttpRequestMessage second = CreateRequest(HttpMethod.Get, "/health", "198.51.100.2");

    using HttpResponseMessage firstResponse = await client.SendAsync(first);
    using HttpResponseMessage secondResponse = await client.SendAsync(second);

    Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
  }

  [Fact]
  public async Task AuthIpWhitelist_UsesForwardedAddressAndCidrRange()
  {
    await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
    {
      ["TransferCs:TrustedProxies"] = "*",
      ["TransferCs:HttpAuthUser"] = "user",
      ["TransferCs:HttpAuthPass"] = "password",
      ["TransferCs:HttpAuthIpWhitelist"] = "198.51.100.0/24"
    });
    using HttpClient client = factory.CreateClient();
    using HttpRequestMessage request = CreateRequest(HttpMethod.Put, "/put/empty.txt", "198.51.100.42");
    request.Content = new ByteArrayContent([]);

    using HttpResponseMessage response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?> settings)
  {
    return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(settings)));
  }

  private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string forwardedFor)
  {
    HttpRequestMessage request = new(method, path);
    request.Headers.Add("X-Forwarded-For", forwardedFor);
    request.Headers.Add("X-Forwarded-Proto", "https");
    return request;
  }
}
