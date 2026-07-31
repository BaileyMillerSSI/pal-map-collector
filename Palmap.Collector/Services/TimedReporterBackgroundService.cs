using System.Net;
using Palmap.Collector.Health;
using Palmap.CollectorApi.Metrics;
using Palmap.CollectorApi.Services;

namespace Palmap.Collector.Services;

internal abstract class TimedReporterBackgroundService(
    IPalworldApiHealthService palworldHealthService,
    ICollectorDelay collectorDelay,
    ICollectorMetricService collectorMetrics,
    ILogger logger) : BackgroundService
{
    private int _consecutiveSourceFailures;

    protected abstract int ReportIntervalMs { get; }

    protected abstract int FailureRetryIntervalMs { get; }

    protected abstract string ReportDescription { get; }

    protected abstract string MetricsSource { get; }

    internal abstract Task ReportOnce(CancellationToken cancellationToken);

    protected abstract Task ReportFailure(
        CollectorSourceFailure failure,
        CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("{Service} is starting.", GetType().Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await palworldHealthService.WaitUntilHealthy(stoppingToken);
                await ReportOnce(stoppingToken);
                LogSourceRecovery();
                collectorMetrics.RecordReporterSuccess(MetricsSource);
                await WaitAfterSuccessfulReport(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                var failure = ClassifySourceFailure(exception);
                if (exception is HttpRequestException)
                {
                    palworldHealthService.MarkUnhealthy();
                    logger.LogDebug(
                        "Skipped {ReportDescription} after a Palworld REST failure ({ExceptionType}); " +
                        "the shared health gate paused polling.",
                        ReportDescription,
                        exception.GetType().Name);
                }
                else
                {
                    LogSourceFailure(exception);
                }

                collectorMetrics.RecordReporterFailure(
                    MetricsSource,
                    failure == CollectorSourceFailure.Unauthorized ? "unauthorized" : "unavailable");
                await ReportFailure(failure, stoppingToken);

                try
                {
                    await collectorDelay.Delay(FailureRetryIntervalMs, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogDebug("{Service} is stopping.", GetType().Name);
    }

    internal void LogSourceFailure(Exception exception)
    {
        _consecutiveSourceFailures++;
        if (_consecutiveSourceFailures == 1)
        {
            logger.LogWarning(
                "Collecting {ReportDescription} failed ({ExceptionType}); the related snapshot section " +
                "may be stale. The collector will retry; update or restart the collector if this persists.",
                ReportDescription,
                exception.GetType().Name);
            return;
        }

        logger.LogDebug(
            "Collecting {ReportDescription} is still failing ({ExceptionType}, attempt {FailureCount}).",
            ReportDescription,
            exception.GetType().Name,
            _consecutiveSourceFailures);
    }

    internal void LogSourceRecovery()
    {
        if (_consecutiveSourceFailures == 0)
        {
            return;
        }

        logger.LogInformation(
            "Collecting {ReportDescription} recovered after {FailureCount} failed attempts; " +
            "fresh data is available again.",
            ReportDescription,
            _consecutiveSourceFailures);
        _consecutiveSourceFailures = 0;
    }

    internal static CollectorSourceFailure ClassifySourceFailure(Exception exception) =>
        exception is HttpRequestException
        {
            StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        }
            ? CollectorSourceFailure.Unauthorized
            : CollectorSourceFailure.Unavailable;

    protected virtual Task WaitAfterSuccessfulReport(CancellationToken cancellationToken) =>
        collectorDelay.Delay(ReportIntervalMs, cancellationToken);

    protected Task Delay(int milliseconds, CancellationToken cancellationToken) =>
        collectorDelay.Delay(milliseconds, cancellationToken);
}
