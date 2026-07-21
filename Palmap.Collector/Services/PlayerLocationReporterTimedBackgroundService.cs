using Microsoft.Extensions.Options;
using Palmap.CollectorApi.src.Configuration;
using Palmap.CollectorApi.src.Services;

namespace Palmap.Collector.Services
{
    public class PlayerLocationReporterTimedBackgroundService(
        ICollectorApiService collectorApiService,
        IOptionsMonitor<CollectorSettings> collectorSettingsMonitor,
        ILogger<PlayerLocationReporterTimedBackgroundService> logger) : BackgroundService
    {
        private readonly ICollectorApiService _collectorApiService = collectorApiService;
        private readonly IOptionsMonitor<CollectorSettings> _collectorSettingsMonitor = collectorSettingsMonitor;
        private readonly ILogger<PlayerLocationReporterTimedBackgroundService> _logger = logger;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{Service} is starting.", nameof(PlayerLocationReporterTimedBackgroundService));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Reporting player locations...");

                    await _collectorApiService.ReportPlayerLocations();

                    _logger.LogInformation("Player locations reported successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while reporting player locations.");
                }

                var interval = _collectorSettingsMonitor.CurrentValue.PlayerLocationUpdateIntervalMs;

                _logger.LogInformation("Waiting for {Interval} milliseconds before the next report.", interval);

                await Task.Delay((int)interval, stoppingToken);
            }

            _logger.LogInformation("{Service} is stopping.", nameof(PlayerLocationReporterTimedBackgroundService));
        }
    }
}
