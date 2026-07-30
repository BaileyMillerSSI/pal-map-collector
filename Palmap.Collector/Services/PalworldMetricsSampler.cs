using System.Net;
using Microsoft.Extensions.Options;
using Palmap.Collector.Configuration;
using Palmap.Collector.Health;
using Palmap.Collector.Metrics;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Metrics;
using Palmap.CollectorApi.Services;
using Palmap.PalworldApi.Services;

namespace Palmap.Collector.Services;

internal sealed class PalworldMetricsSampler(
    IPalworldApiServiceFactory palworldApiServiceFactory,
    PalworldMetricsCache metricsCache,
    IOptionsMonitor<PrometheusExporterSettings> exporterSettings,
    IOptionsMonitor<CollectorSettings> collectorSettings,
    IPalworldApiHealthService palworldHealthService,
    ICollectorDelay collectorDelay,
    ICollectorMetricService collectorMetrics,
    ILogger<PalworldMetricsSampler> logger) : BackgroundService
{
    public const string SourceName = "metrics";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("{Service} is starting.", nameof(PalworldMetricsSampler));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await palworldHealthService.WaitUntilHealthy(stoppingToken);
                await SampleOnce(stoppingToken);
                await collectorDelay.Delay(exporterSettings.CurrentValue.SampleIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (exception is HttpRequestException)
                {
                    palworldHealthService.MarkUnhealthy();
                    logger.LogDebug(
                        "Skipped server metrics after a Palworld REST failure ({ExceptionType}); " +
                        "the shared health gate paused polling.",
                        exception.GetType().Name);
                }

                collectorMetrics.RecordReporterFailure(
                    SourceName,
                    ClassifyFailureReason(exception));

                try
                {
                    await collectorDelay.Delay(
                        collectorSettings.CurrentValue.FailureRetryIntervalMs,
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogDebug("{Service} is stopping.", nameof(PalworldMetricsSampler));
    }

    internal async Task SampleOnce(CancellationToken cancellationToken)
    {
        using var palworldApiService = palworldApiServiceFactory.Create();
        var metrics = await palworldApiService.ServerMetrics(cancellationToken);
        metricsCache.Update(metrics);
        collectorMetrics.RecordReporterSuccess(SourceName);
        logger.LogDebug(
            "Collected server metrics ({PlayerCount}/{MaxPlayerCount} players, {ServerFps} fps).",
            metrics.CurrentPlayerCount,
            metrics.MaxPlayerCount,
            metrics.ServerFps);
    }

    internal static string ClassifyFailureReason(Exception exception) =>
        exception is HttpRequestException
        {
            StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        }
            ? "unauthorized"
            : "unavailable";
}
