using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Services;

public class PurgeBackgroundService : BackgroundService
{
    private readonly IStorageProvider _storage;
    private readonly TransferCsOptions _options;
    private readonly ILogger<PurgeBackgroundService> _logger;

    public PurgeBackgroundService(
        IStorageProvider storage,
        IOptions<TransferCsOptions> options,
        ILogger<PurgeBackgroundService> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.PurgeDays <= 0 || _options.PurgeIntervalHours <= 0)
            return;

        var interval = TimeSpan.FromHours(_options.PurgeIntervalHours);
        var maxAge = TimeSpan.FromDays(_options.PurgeDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                _logger.LogInformation("Running purge for files older than {PurgeDays} days", _options.PurgeDays);
                await _storage.PurgeAsync(maxAge, stoppingToken);
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
}
