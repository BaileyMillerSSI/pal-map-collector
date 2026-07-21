using Palmap.Collector.Health;

namespace Palmap.Collector.Services;

internal abstract class TimedReporterBackgroundService(
    IPalworldApiHealthService palworldHealthService,
    ICollectorDelay collectorDelay,
    ILogger logger) : BackgroundService
{
    protected abstract int ReportIntervalMs { get; }

    protected abstract int FailureRetryIntervalMs { get; }

    protected abstract string ReportDescription { get; }

    internal abstract Task ReportOnce(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Service} is starting.", GetType().Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await palworldHealthService.WaitUntilHealthy(stoppingToken);
                await ReportOnce(stoppingToken);
                await collectorDelay.Delay(ReportIntervalMs, stoppingToken);
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
                }

                logger.LogError(exception, "An error occurred while reporting {ReportDescription}.", ReportDescription);

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

        logger.LogInformation("{Service} is stopping.", GetType().Name);
    }
}
