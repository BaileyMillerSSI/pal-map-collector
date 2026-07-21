using Microsoft.Extensions.Options;
using Palmap.Collector.Health;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Services;
using Palmap.PalworldApi.Services;

namespace Palmap.Collector.Services;

internal sealed class GameDataReportTimedBackgroundService(
    IPalworldApiServiceFactory palworldApiServiceFactory,
    ICollectorApiService collectorApiService,
    IOptionsMonitor<CollectorSettings> collectorSettings,
    IPalworldApiHealthService palworldHealthService,
    ICollectorDelay collectorDelay,
    ILogger<GameDataReportTimedBackgroundService> logger)
    : TimedReporterBackgroundService(palworldHealthService, collectorDelay, logger)
{
    protected override int ReportIntervalMs => collectorSettings.CurrentValue.GameDataUpdateIntervalMs;

    protected override int FailureRetryIntervalMs => collectorSettings.CurrentValue.FailureRetryIntervalMs;

    protected override string ReportDescription => "game data";

    internal override async Task ReportOnce(CancellationToken cancellationToken)
    {
        using var palworldApiService = palworldApiServiceFactory.Create();
        var snapshot = await palworldApiService.WorldActorSnapshot(cancellationToken);
        await collectorApiService.ReportGameData(snapshot, cancellationToken);
        logger.LogInformation("Reported {ActorCount} world actors.", snapshot.ActorData.Count);
    }

    protected override Task ReportFailure(
        CollectorSourceFailure failure,
        CancellationToken cancellationToken) =>
        collectorApiService.ReportFailure(CollectorSourceSection.World, failure, cancellationToken);
}
