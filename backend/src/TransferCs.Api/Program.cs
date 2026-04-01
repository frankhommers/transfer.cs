using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Endpoints;
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

// Services
builder.Services.AddSingleton<IStorageProvider>(new LocalStorageProvider(config.BasePath));
builder.Services.AddSingleton<MetadataService>();
builder.Services.AddHostedService<PurgeBackgroundService>();
builder.Services.AddHttpClient();

// Rate limiting (conditional)
if (config.RateLimitRequestsPerMinute > 0)
  builder.Services.AddRateLimiter(options =>
  {
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
      RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
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

// Kestrel max request body size (default 30MB is too small for file uploads)
builder.WebHost.ConfigureKestrel(options =>
{
  options.Limits.MaxRequestBodySize = config.MaxUploadSizeBytes > 0
    ? config.MaxUploadSizeBytes
    : null; // null = unlimited
});

WebApplication app = builder.Build();

// Middleware pipeline (order matters)
app.UseMiddleware<LoveHeaderMiddleware>();
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
app.MapGet("/health", (IStorageProvider storage) =>
  Results.Json(new TransferCs.Api.Models.HealthResponse
  {
    Status = "healthy",
    Storage = storage.Type
  }, TransferCs.Api.Models.AppJsonContext.Default.HealthResponse));

app.MapGet("/api/config", (IOptions<TransferCsOptions> opts) =>
  Results.Json(new TransferCs.Api.Models.PublicConfig
  {
    Title = opts.Value.Title,
    PurgeDays = opts.Value.PurgeDays,
    MaxUploadSizeKb = opts.Value.MaxUploadSizeKb
  }, TransferCs.Api.Models.AppJsonContext.Default.PublicConfig));

app.MapViewEndpoints();
app.MapUploadEndpoints();
app.MapDownloadEndpoints();
app.MapDeleteEndpoints();
app.MapBundleEndpoints();
app.MapScanEndpoints();
app.MapPreviewEndpoints();
app.MapSkillEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program
{
}