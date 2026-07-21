using Microsoft.Extensions.Options;
using Palmap.Collector.Services;
using Palmap.CollectorApi.Configuration;
using Palmap.PalworldApi.Services;

namespace Palmap.Collector.Health;

internal sealed class PalworldApiHealthService(
    IPalworldApiServiceFactory palworldApiServiceFactory,
    IOptionsMonitor<CollectorSettings> collectorSettings,
    ICollectorDelay collectorDelay,
    TimeProvider timeProvider,
    ILogger<PalworldApiHealthService> logger) : IPalworldApiHealthService, IDisposable
{
    private const int Unknown = -1;
    private const int Unhealthy = 0;
    private const int Healthy = 1;

    private readonly SemaphoreSlim _probeLock = new(1, 1);
    private int _state = Unknown;
    private long _lastProbeTimestamp;

    public async Task<bool> IsHealthy(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedState(out var isHealthy))
        {
            return isHealthy;
        }

        await _probeLock.WaitAsync(cancellationToken);

        try
        {
            if (TryGetCachedState(out isHealthy))
            {
                return isHealthy;
            }

            using var palworldApiService = palworldApiServiceFactory.Create();
            isHealthy = await palworldApiService.Ping(cancellationToken);
            SetState(isHealthy ? Healthy : Unhealthy);
            return isHealthy;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    public async Task WaitUntilHealthy(CancellationToken cancellationToken = default)
    {
        while (!await IsHealthy(cancellationToken))
        {
            await collectorDelay.Delay(
                collectorSettings.CurrentValue.FailureRetryIntervalMs,
                cancellationToken);
        }
    }

    public void MarkUnhealthy() => SetState(Unhealthy);

    public void Dispose() => _probeLock.Dispose();

    private bool TryGetCachedState(out bool isHealthy)
    {
        var state = Volatile.Read(ref _state);
        var lastProbeTimestamp = Interlocked.Read(ref _lastProbeTimestamp);

        if (state == Unknown || lastProbeTimestamp == 0)
        {
            isHealthy = false;
            return false;
        }

        var cacheDuration = TimeSpan.FromMilliseconds(
            collectorSettings.CurrentValue.PalworldHealthCacheDurationMs);

        if (timeProvider.GetElapsedTime(lastProbeTimestamp) >= cacheDuration)
        {
            isHealthy = false;
            return false;
        }

        isHealthy = state == Healthy;
        return true;
    }

    private void SetState(int newState)
    {
        var previousState = Interlocked.Exchange(ref _state, newState);
        Interlocked.Exchange(ref _lastProbeTimestamp, timeProvider.GetTimestamp());

        if (previousState == newState)
        {
            return;
        }

        if (newState == Healthy)
        {
            logger.LogInformation("Palworld REST API is available.");
        }
        else
        {
            logger.LogWarning("Palworld REST API is unavailable; reporters will wait before retrying.");
        }
    }
}
