using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Services;

public class PurgeBackgroundService : BackgroundService
{
  private readonly SiteStorageFactory _storageFactory;
  private readonly SiteResolver _siteResolver;
  private readonly TransferCsOptions _options;
  private readonly ILogger<PurgeBackgroundService> _logger;

  public PurgeBackgroundService(
    SiteStorageFactory storageFactory,
    SiteResolver siteResolver,
    IOptions<TransferCsOptions> options,
    ILogger<PurgeBackgroundService> logger)
  {
    _storageFactory = storageFactory;
    _siteResolver = siteResolver;
    _options = options.Value;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (_options.PurgeIntervalHours <= 0)
      return;

    TimeSpan interval = TimeSpan.FromHours(_options.PurgeIntervalHours);
    while (!stoppingToken.IsCancellationRequested)
      try
      {
        IEnumerable<ResolvedSite> sites = _siteResolver.IsMultiSite
          ? _siteResolver.Sites
          : [_siteResolver.LegacySite];
        foreach (ResolvedSite site in sites.Where(site => site.Options.PurgeDays > 0))
        {
          try
          {
            _logger.LogInformation("Running purge for site {SiteId}: files older than {PurgeDays} days",
              site.Id, site.Options.PurgeDays);
            await _storageFactory.Get(site).PurgeAsync(TimeSpan.FromDays(site.Options.PurgeDays), stoppingToken);
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "Error purging site {SiteId}", site.Id);
          }
        }
        await Task.Delay(interval, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during purge");
      }
  }
}
