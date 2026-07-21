namespace Palmap.Collector.Health;

internal interface IPalworldApiHealthService
{
    Task<bool> IsHealthy(CancellationToken cancellationToken = default);

    Task WaitUntilHealthy(CancellationToken cancellationToken = default);

    void MarkUnhealthy();
}
