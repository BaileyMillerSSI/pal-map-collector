using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Palmap.Collector.Health;
using Palmap.Collector.Services;
using Palmap.CollectorApi.Configuration;

namespace Palmap.UnitTests;

public sealed class ReporterAndHealthTests
{
    [Fact]
    public async Task ReportersFetchAndForwardTheirMatchingPayloads()
    {
        var palworld = new StubPalworldApiService();
        var collector = new RecordingCollectorApiService();
        var options = new StaticOptionsMonitor<CollectorSettings>(new());
        var health = new StubPalworldApiHealthService();
        var delay = new RecordingCollectorDelay();

        await new PlayerLocationReporterTimedBackgroundService(
            palworld,
            collector,
            options,
            health,
            delay,
            NullLogger<PlayerLocationReporterTimedBackgroundService>.Instance)
            .ReportOnce(CancellationToken.None);

        await new GameDataReportTimedBackgroundService(
            palworld,
            collector,
            options,
            health,
            delay,
            NullLogger<GameDataReportTimedBackgroundService>.Instance)
            .ReportOnce(CancellationToken.None);

        await new GameServerSettingsReportTimedBackgroundService(
            palworld,
            collector,
            options,
            health,
            delay,
            NullLogger<GameServerSettingsReportTimedBackgroundService>.Instance)
            .ReportOnce(CancellationToken.None);

        Assert.Same(palworld.Players, collector.Players);
        Assert.Same(palworld.Snapshot, collector.Snapshot);
        Assert.Same(palworld.Settings, collector.Settings);
    }

    [Fact]
    public async Task ReportersUseTheirMatchingIntervalsAndStopDuringDelay()
    {
        var options = new StaticOptionsMonitor<CollectorSettings>(new CollectorSettings
        {
            PlayerLocationUpdateIntervalMs = 11,
            GameDataUpdateIntervalMs = 22,
            ServerSettingsUpdateIntervalMs = 33
        });
        var palworld = new StubPalworldApiService();
        var collector = new RecordingCollectorApiService();
        var health = new StubPalworldApiHealthService();

        var playerDelay = new RecordingCollectorDelay();
        await AssertScheduled(
            new PlayerLocationReporterTimedBackgroundService(
                palworld,
                collector,
                options,
                health,
                playerDelay,
                NullLogger<PlayerLocationReporterTimedBackgroundService>.Instance),
            playerDelay,
            11);

        var gameDataDelay = new RecordingCollectorDelay();
        await AssertScheduled(
            new GameDataReportTimedBackgroundService(
                palworld,
                collector,
                options,
                health,
                gameDataDelay,
                NullLogger<GameDataReportTimedBackgroundService>.Instance),
            gameDataDelay,
            22);

        var settingsDelay = new RecordingCollectorDelay();
        await AssertScheduled(
            new GameServerSettingsReportTimedBackgroundService(
                palworld,
                collector,
                options,
                health,
                settingsDelay,
                NullLogger<GameServerSettingsReportTimedBackgroundService>.Instance),
            settingsDelay,
            33);
    }

    [Fact]
    public async Task HttpFailureInvalidatesHealthAndUsesFailureRetryInterval()
    {
        var palworld = new StubPalworldApiService
        {
            PlayerListException = new HttpRequestException("server unavailable")
        };
        var options = new StaticOptionsMonitor<CollectorSettings>(new CollectorSettings
        {
            FailureRetryIntervalMs = 77
        });
        var health = new StubPalworldApiHealthService();
        var delay = new RecordingCollectorDelay();
        var worker = new PlayerLocationReporterTimedBackgroundService(
            palworld,
            new RecordingCollectorApiService(),
            options,
            health,
            delay,
            NullLogger<PlayerLocationReporterTimedBackgroundService>.Instance);

        await AssertScheduled(worker, delay, 77);

        Assert.Equal(1, health.MarkUnhealthyCallCount);
    }

    [Fact]
    public async Task ReporterDoesNotCallPalworldWhileSharedHealthIsUnhealthy()
    {
        var collector = new RecordingCollectorApiService();
        var health = new StubPalworldApiHealthService { IsHealthyResult = false };
        var palworld = new StubPalworldApiService();
        var worker = new PlayerLocationReporterTimedBackgroundService(
            palworld,
            collector,
            new StaticOptionsMonitor<CollectorSettings>(new()),
            health,
            new RecordingCollectorDelay(),
            NullLogger<PlayerLocationReporterTimedBackgroundService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(timeout.Token);
        await health.WaitStarted.WaitAsync(timeout.Token);

        Assert.Null(collector.Players);
        Assert.Equal(0, palworld.PlayerListCallCount);

        await worker.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task SharedHealthServiceCachesSuccessAndWaitsThroughStartupFailure()
    {
        var palworld = new StubPalworldApiService();
        var cachedOptions = new StaticOptionsMonitor<CollectorSettings>(new CollectorSettings
        {
            PalworldHealthCacheDurationMs = 5_000
        });
        using var cachedHealth = CreateHealthService(palworld, cachedOptions);

        Assert.True(await cachedHealth.IsHealthy());
        Assert.True(await cachedHealth.IsHealthy());
        Assert.Equal(1, palworld.PingCallCount);

        var startingPalworld = new StubPalworldApiService();
        startingPalworld.SetPingResults(false, true);
        var retryOptions = new StaticOptionsMonitor<CollectorSettings>(new CollectorSettings
        {
            FailureRetryIntervalMs = 1,
            PalworldHealthCacheDurationMs = 1
        });
        using var startupHealth = CreateHealthService(startingPalworld, retryOptions);

        await startupHealth.WaitUntilHealthy();

        Assert.Equal(2, startingPalworld.PingCallCount);
    }

    [Theory]
    [InlineData(true, HealthStatus.Healthy)]
    [InlineData(false, HealthStatus.Unhealthy)]
    public async Task ReadinessUsesSharedPalworldHealthState(bool isHealthy, HealthStatus expectedStatus)
    {
        var health = new StubPalworldApiHealthService { IsHealthyResult = isHealthy };
        var result = await new PalworldApiHealthCheck(health)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expectedStatus, result.Status);
    }

    private static PalworldApiHealthService CreateHealthService(
        StubPalworldApiService palworld,
        StaticOptionsMonitor<CollectorSettings> options) => new(
            palworld,
            options,
            new CollectorDelay(),
            TimeProvider.System,
            NullLogger<PalworldApiHealthService>.Instance);

    private static async Task AssertScheduled(
        TimedReporterBackgroundService worker,
        RecordingCollectorDelay delay,
        int expectedInterval)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(timeout.Token);
        Assert.Equal(expectedInterval, await delay.ReadNext(timeout.Token));
        await worker.StopAsync(timeout.Token);
    }
}
