using Microsoft.Extensions.Options;
using Palmap.Collector.Health;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Services;
using Palmap.PalworldApi.Services;

namespace Palmap.Collector.Services;

internal sealed class PlayerLocationReporterTimedBackgroundService(
    IPalworldApiServiceFactory palworldApiServiceFactory,
    ICollectorApiService collectorApiService,
    IOptionsMonitor<CollectorSettings> collectorSettings,
    IPalworldApiHealthService palworldHealthService,
    ICollectorDelay collectorDelay,
    ILogger<PlayerLocationReporterTimedBackgroundService> logger)
    : TimedReporterBackgroundService(palworldHealthService, collectorDelay, logger)
{
    protected override int ReportIntervalMs => collectorSettings.CurrentValue.PlayerLocationUpdateIntervalMs;

    protected override int FailureRetryIntervalMs => collectorSettings.CurrentValue.FailureRetryIntervalMs;

    protected override string ReportDescription => "player locations";

    internal override async Task ReportOnce(CancellationToken cancellationToken)
    {
        using var palworldApiService = palworldApiServiceFactory.Create();
        var players = await palworldApiService.PlayerList(cancellationToken);
        await collectorApiService.ReportPlayerLocations(players, cancellationToken);
        logger.LogInformation("Reported {PlayerCount} player locations.", players.Players.Count);
    }
}
