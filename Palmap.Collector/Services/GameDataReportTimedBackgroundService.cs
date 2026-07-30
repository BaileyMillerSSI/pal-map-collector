using Microsoft.Extensions.Options;
using Palmap.Collector.Health;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Metrics;
using Palmap.CollectorApi.Services;
using Palmap.CollectorApi.Services.Internal;
using Palmap.PalworldApi.Services;

namespace Palmap.Collector.Services;

internal sealed class GameDataReportTimedBackgroundService(
    IPalworldApiServiceFactory palworldApiServiceFactory,
    ICollectorApiService collectorApiService,
    GameDataRefreshSignal refreshSignal,
    IOptionsMonitor<CollectorSettings> collectorSettings,
    IPalworldApiHealthService palworldHealthService,
    ICollectorDelay collectorDelay,
    ICollectorMetricService collectorMetrics,
    ILogger<GameDataReportTimedBackgroundService> logger)
    : TimedReporterBackgroundService(palworldHealthService, collectorDelay, collectorMetrics, logger)
{
    private long _completedRevision = -1;

    protected override int ReportIntervalMs => collectorSettings.CurrentValue.GameDataUpdateIntervalMs;

    protected override int FailureRetryIntervalMs => collectorSettings.CurrentValue.FailureRetryIntervalMs;

    protected override string ReportDescription => "game data";

    protected override string MetricsSource => "world";

    internal override async Task ReportOnce(CancellationToken cancellationToken)
    {
        var requestedRevision = collectorApiService.CaptureWorldRevision();
        using var palworldApiService = palworldApiServiceFactory.Create();
        var snapshot = await palworldApiService.WorldActorSnapshot(cancellationToken);
        await collectorApiService.ReportGameData(snapshot, requestedRevision, cancellationToken);
        _completedRevision = requestedRevision;
        logger.LogDebug("Collected {ActorCount} world actors.", snapshot.ActorData.Count);
    }

    protected override async Task WaitAfterSuccessfulReport(CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var interval = Delay(ReportIntervalMs, waitCancellation.Token);
        var refresh = refreshSignal
            .WaitForRevisionAfter(_completedRevision, waitCancellation.Token)
            .AsTask();
        await Task.WhenAny(interval, refresh);
        await waitCancellation.CancelAsync();
        await Observe(interval, waitCancellation.Token);
        await Observe(refresh, waitCancellation.Token);
    }

    private static async Task Observe(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    protected override Task ReportFailure(
        CollectorSourceFailure failure,
        CancellationToken cancellationToken) =>
        collectorApiService.ReportFailure(CollectorSourceSection.World, failure, cancellationToken);
}
