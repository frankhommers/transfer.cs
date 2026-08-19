using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
using TransferCs.Api.Middleware;

namespace TransferCs.Api.Tests.Middleware;

public class SecurityRegressionTests
{
  [Theory]
  [InlineData("not-an-ip")]
  [InlineData(",")]
  public void InvalidTrustedProxy_FailsClosed(string value)
  {
    ForwardedHeadersOptions options = new();

    Assert.Throws<ArgumentException>(() => ForwardedHeadersSetup.Configure(options, value));
  }

  [Theory]
  [InlineData("not-an-ip")]
  [InlineData(",")]
  public void InvalidIpWhitelist_FailsClosed(string value)
  {
    Assert.Throws<ArgumentException>(() => new IpListMatcher(value));
  }

  [Fact]
  public async Task ForceHttps_DoesNotTrustRawForwardedProtoHeader()
  {
    bool nextCalled = false;
    ForceHttpsMiddleware middleware = new(
      _ =>
      {
        nextCalled = true;
        return Task.CompletedTask;
      },
      Options.Create(new TransferCsOptions { ForceHttps = true }));
    DefaultHttpContext context = new();
    context.Request.Scheme = "http";
    context.Request.Host = new HostString("example.test");
    context.Request.Headers["X-Forwarded-Proto"] = "https";

    await middleware.InvokeAsync(context);

    Assert.False(nextCalled);
    Assert.Equal(StatusCodes.Status308PermanentRedirect, context.Response.StatusCode);
  }

  [Fact]
  public void UrlHelper_DoesNotTrustRawForwardedProtoHeader()
  {
    DefaultHttpContext context = new();
    context.Request.Scheme = "http";
    context.Request.Host = new HostString("example.test");
    context.Request.Headers["X-Forwarded-Proto"] = "https";

    string url = UrlHelper.ResolveUrl(context.Request, "/file.txt", new TransferCsOptions());

    Assert.Equal("http://example.test/file.txt", url);
  }
}
