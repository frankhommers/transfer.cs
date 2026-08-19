using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Endpoints;
using TransferCs.Api.Helpers;
using TransferCs.Api.Middleware;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<TransferCsOptions>(builder.Configuration.GetSection(TransferCsOptions.SectionName));
TransferCsOptions config = builder.Configuration.GetSection(TransferCsOptions.SectionName).Get<TransferCsOptions>() ??
                            new TransferCsOptions();
if (config.RandomTokenLength is < 6 or > 128)
  throw new InvalidOperationException("TransferCs:RandomTokenLength must be between 6 and 128.");

builder.Services.AddOptions<ForwardedHeadersOptions>()
  .Configure<IOptions<TransferCsOptions>>((options, transferOptions) =>
    ForwardedHeadersSetup.Configure(options, transferOptions.Value.TrustedProxies));

// Services
builder.Services.AddSingleton<SiteResolver>();
builder.Services.AddSingleton<SiteStorageFactory>();
builder.Services.AddSingleton<SiteDataMigration>();
builder.Services.AddSingleton<KeyedLock>();
builder.Services.AddScoped<SiteContext>();
builder.Services.AddScoped<IStorageProvider>(services =>
  services.GetRequiredService<SiteStorageFactory>().Get(services.GetRequiredService<SiteContext>().Site));
builder.Services.AddScoped<MetadataService>();
builder.Services.AddHostedService<PurgeBackgroundService>();
builder.Services.AddHttpClient();

// Rate limiting (conditional)
if (config.RateLimitRequestsPerMinute > 0)
  builder.Services.AddRateLimiter(options =>
  {
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
      RateLimitPartition.GetFixedWindowLimiter(
        ClientIpHelper.Get(context) is { Length: > 0 } ip ? ip : "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
          PermitLimit = config.RateLimitRequestsPerMinute,
          Window = TimeSpan.FromMinutes(1)
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
  });

// CORS (conditional)
if (!string.IsNullOrEmpty(config.CorsDomains))
{
  string[] origins =
    config.CorsDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  builder.Services.AddCors(options =>
  {
    options.AddDefaultPolicy(policy =>
    {
      policy.WithOrigins(origins)
        .AllowAnyMethod()
        .AllowAnyHeader();
    });
  });
}

// Kestrel limits for large file uploads
builder.WebHost.ConfigureKestrel(options =>
{
  options.Limits.MaxRequestBodySize = UploadLimitHelper.ResolveKestrelLimit(config);
  options.Limits.MinRequestBodyDataRate = null;
  options.Limits.KeepAliveTimeout = TimeSpan.FromHours(24);
});

WebApplication app = builder.Build();
app.Services.GetRequiredService<SiteDataMigration>().Run();

// Middleware pipeline (order matters)
app.UseForwardedHeaders();
app.UseMiddleware<SiteResolutionMiddleware>();

app.UseMiddleware<LoveHeaderMiddleware>();
app.UseMiddleware<AdminSecurityHeadersMiddleware>();
app.UseMiddleware<IpFilterMiddleware>();
app.UseMiddleware<ForceHttpsMiddleware>();

if (!string.IsNullOrEmpty(config.CorsDomains))
  app.UseCors();

if (config.RateLimitRequestsPerMinute > 0)
  app.UseRateLimiter();

app.UseMiddleware<BasicAuthMiddleware>();

// Static file serving (frontend SPA)
app.MapStaticAssets();

// Endpoints
app.MapGet("/health", (SiteStorageFactory storageFactory) =>
  Results.Json(new TransferCs.Api.Models.HealthResponse
  {
    Status = "healthy",
    Storage = storageFactory.Type
  }, TransferCs.Api.Models.AppJsonContext.Default.HealthResponse));

app.MapGet("/api/config", (SiteContext siteContext) =>
  Results.Json(new TransferCs.Api.Models.PublicConfig
  {
    Title = siteContext.Site.Options.Title,
    PurgeDays = siteContext.Site.Options.PurgeDays,
    MaxUploadSizeKb = siteContext.Site.Options.MaxUploadSizeKb
  }, TransferCs.Api.Models.AppJsonContext.Default.PublicConfig));

app.MapViewEndpoints();
app.MapUploadEndpoints();
app.MapDownloadEndpoints();
app.MapDeleteEndpoints();
app.MapBundleEndpoints();
app.MapScanEndpoints();
app.MapPreviewEndpoints();
app.MapSkillEndpoints();
app.MapAdminEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program
{
}
