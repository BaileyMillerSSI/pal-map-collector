using Microsoft.Extensions.Options;
using Palmap.Collector.Health;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Metrics;
using Palmap.CollectorApi.Services;
using Palmap.PalworldApi.Services;

namespace Palmap.Collector.Services;

internal sealed class GameServerSettingsReportTimedBackgroundService(
    IPalworldApiServiceFactory palworldApiServiceFactory,
    ICollectorApiService collectorApiService,
    IOptionsMonitor<CollectorSettings> collectorSettings,
    IPalworldApiHealthService palworldHealthService,
    ICollectorDelay collectorDelay,
    ICollectorMetricService collectorMetrics,
    ILogger<GameServerSettingsReportTimedBackgroundService> logger)
    : TimedReporterBackgroundService(palworldHealthService, collectorDelay, collectorMetrics, logger)
{
    protected override int ReportIntervalMs => collectorSettings.CurrentValue.ServerSettingsUpdateIntervalMs;

    protected override int FailureRetryIntervalMs => collectorSettings.CurrentValue.FailureRetryIntervalMs;

    protected override string ReportDescription => "server settings";

    protected override string MetricsSource => "settings";

    internal override async Task ReportOnce(CancellationToken cancellationToken)
    {
        using var palworldApiService = palworldApiServiceFactory.Create();
        var settings = await palworldApiService.ServerSettings(cancellationToken);
        await collectorApiService.ReportServerSettings(settings, cancellationToken);
        logger.LogDebug("Collected server settings.");
    }

    protected override Task ReportFailure(
        CollectorSourceFailure failure,
        CancellationToken cancellationToken) =>
        collectorApiService.ReportFailure(CollectorSourceSection.Server, failure, cancellationToken);
}
