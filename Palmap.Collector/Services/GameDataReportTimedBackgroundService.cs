using Microsoft.Extensions.Options;
using Palmap.CollectorApi.src.Configuration;
using Palmap.CollectorApi.src.Services;

namespace Palmap.Collector.Services
{
    public class GameDataReportTimedBackgroundService(
        ICollectorApiService collectorApiService,
        IOptionsMonitor<CollectorSettings> collectorSettingsMonitor,
        ILogger<GameDataReportTimedBackgroundService> logger) : BackgroundService
    {
        private readonly ICollectorApiService _collectorApiService = collectorApiService;
        private readonly IOptionsMonitor<CollectorSettings> _collectorSettingsMonitor = collectorSettingsMonitor;
        private readonly ILogger<GameDataReportTimedBackgroundService> _logger = logger;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{Service} is starting.", nameof(GameDataReportTimedBackgroundService));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Reporting game data...");

                    await _collectorApiService.ReportGameData();

                    _logger.LogInformation("Game data reported successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while reporting game data.");
                }

                var interval = _collectorSettingsMonitor.CurrentValue.GameDataUpdateIntervalMs;

                _logger.LogInformation("Waiting for {Interval} milliseconds before the next report.", interval);

                await Task.Delay((int)interval, stoppingToken);
            }

            _logger.LogInformation("{Service} is stopping.", nameof(GameDataReportTimedBackgroundService));
        }
    }
}
