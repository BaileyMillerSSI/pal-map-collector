using Palmap.Collector.Health;

namespace Palmap.UnitTests;

internal sealed class StubPalworldApiHealthService : IPalworldApiHealthService
{
    private readonly TaskCompletionSource _waitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsHealthyResult { get; set; } = true;

    public int MarkUnhealthyCallCount { get; private set; }

    public Task WaitStarted => _waitStarted.Task;

    public Task<bool> IsHealthy(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsHealthyResult);

    public Task WaitUntilHealthy(CancellationToken cancellationToken = default)
    {
        _waitStarted.TrySetResult();

        return IsHealthyResult
            ? Task.CompletedTask
            : Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public void MarkUnhealthy()
    {
        IsHealthyResult = false;
        MarkUnhealthyCallCount++;
    }
}
