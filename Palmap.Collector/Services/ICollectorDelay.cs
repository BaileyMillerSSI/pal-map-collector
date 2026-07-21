namespace Palmap.Collector.Services;

internal interface ICollectorDelay
{
    Task Delay(int milliseconds, CancellationToken cancellationToken);
}
