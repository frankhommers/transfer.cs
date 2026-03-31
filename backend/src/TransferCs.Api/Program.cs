using System.Threading.RateLimiting;
using TransferCs.Api.Configuration;
using TransferCs.Api.Endpoints;
using TransferCs.Api.Middleware;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<TransferCsOptions>(builder.Configuration.GetSection(TransferCsOptions.SectionName));
var config = builder.Configuration.GetSection(TransferCsOptions.SectionName).Get<TransferCsOptions>() ?? new TransferCsOptions();

// Services
builder.Services.AddSingleton<IStorageProvider>(new LocalStorageProvider(config.BasePath));
builder.Services.AddSingleton<MetadataService>();
builder.Services.AddHostedService<PurgeBackgroundService>();
builder.Services.AddHttpClient();

// Rate limiting (conditional)
if (config.RateLimitRequestsPerMinute > 0)
{
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
}

// CORS (conditional)
if (!string.IsNullOrEmpty(config.CorsDomains))
{
    var origins = config.CorsDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

var app = builder.Build();

// Middleware pipeline (order matters)
app.UseMiddleware<LoveHeaderMiddleware>();
app.UseMiddleware<IpFilterMiddleware>();
app.UseMiddleware<ForceHttpsMiddleware>();

if (!string.IsNullOrEmpty(config.CorsDomains))
    app.UseCors();

if (config.RateLimitRequestsPerMinute > 0)
    app.UseRateLimiter();

app.UseMiddleware<BasicAuthMiddleware>();

// Endpoints
app.MapGet("/health.html", () => "Approaching Neutral Zone, all systems normal and functioning.");

app.MapViewEndpoints();
app.MapUploadEndpoints();
app.MapDownloadEndpoints();
app.MapDeleteEndpoints();
app.MapBundleEndpoints();
app.MapScanEndpoints();
app.MapPreviewEndpoints();

app.Run();

public partial class Program { }
