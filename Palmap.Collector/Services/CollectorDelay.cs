namespace Palmap.Collector.Services;

internal sealed class CollectorDelay : ICollectorDelay
{
    public Task Delay(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(milliseconds, cancellationToken);
}
