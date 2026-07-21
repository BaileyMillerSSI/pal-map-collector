using Microsoft.Extensions.Options;
using Palmap.CollectorApi.src.Configuration;
using Palmap.CollectorApi.src.Services;

namespace Palmap.Collector.Services
{
    public class GameServerSettingsReportTimedBackgroundService(
        ICollectorApiService collectorApiService,
        IOptionsMonitor<CollectorSettings> collectorSettingsMonitor,
        ILogger<GameServerSettingsReportTimedBackgroundService> logger) : BackgroundService
    {
        private readonly ICollectorApiService _collectorApiService = collectorApiService;
        private readonly IOptionsMonitor<CollectorSettings> _collectorSettingsMonitor = collectorSettingsMonitor;
        private readonly ILogger<GameServerSettingsReportTimedBackgroundService> _logger = logger;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{Service} is starting.", nameof(GameServerSettingsReportTimedBackgroundService));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Reporting server settings...");

                    await _collectorApiService.ReportServerSettings();

                    _logger.LogInformation("Server settings reported successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while reporting server settings.");
                }

                var interval = _collectorSettingsMonitor.CurrentValue.GameDataUpdateIntervalMs;

                _logger.LogInformation("Waiting for {Interval} milliseconds before the next report.", interval);

                await Task.Delay((int)interval, stoppingToken);
            }

            _logger.LogInformation("{Service} is stopping.", nameof(GameServerSettingsReportTimedBackgroundService));
        }
    }
}
