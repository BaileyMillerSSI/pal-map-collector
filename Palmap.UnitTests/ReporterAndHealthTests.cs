using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Palmap.Collector.Health;
using Palmap.Collector.Services;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Metrics;
using Palmap.CollectorApi.Services;
using Palmap.CollectorApi.Services.Internal;
using Palmap.PalworldApi.Models;

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
            new CollectorMetrics(TimeProvider.System),
            NullLogger<PlayerLocationReporterTimedBackgroundService>.Instance)
            .ReportOnce(CancellationToken.None);

        await new GameDataReportTimedBackgroundService(
            palworld,
            collector,
            new GameDataRefreshSignal(),
            options,
            health,
            delay,
            new CollectorMetrics(TimeProvider.System),
            NullLogger<GameDataReportTimedBackgroundService>.Instance)
            .ReportOnce(CancellationToken.None);

        await new GameServerSettingsReportTimedBackgroundService(
            palworld,
            collector,
            options,
            health,
            delay,
            new CollectorMetrics(TimeProvider.System),
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
                new CollectorMetrics(TimeProvider.System),
                NullLogger<PlayerLocationReporterTimedBackgroundService>.Instance),
            playerDelay,
            11);

        var gameDataDelay = new RecordingCollectorDelay();
        await AssertScheduled(
            new GameDataReportTimedBackgroundService(
                palworld,
                collector,
                new GameDataRefreshSignal(),
                options,
                health,
                gameDataDelay,
                new CollectorMetrics(TimeProvider.System),
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
                new CollectorMetrics(TimeProvider.System),
                NullLogger<GameServerSettingsReportTimedBackgroundService>.Instance),
            settingsDelay,
            33);
    }

    [Fact]
    public async Task GameDataReporterWakesImmediatelyForTheLatestRefreshRequest()
    {
        var palworld = new StubPalworldApiService();
        var collector = new RecordingCollectorApiService();
        var refreshSignal = new GameDataRefreshSignal();
        var delay = new RecordingCollectorDelay();
        var worker = new GameDataReportTimedBackgroundService(
            palworld,
            collector,
            refreshSignal,
            new StaticOptionsMonitor<CollectorSettings>(new()),
            new StubPalworldApiHealthService(),
            delay,
            new CollectorMetrics(TimeProvider.System),
            NullLogger<GameDataReportTimedBackgroundService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(timeout.Token);
        Assert.Equal(30_000, await delay.ReadNext(timeout.Token));
        collector.WorldRevision = 2;
        refreshSignal.Request(1);
        refreshSignal.Request(2);

        await palworld.SecondWorldActorSnapshotCall.Task.WaitAsync(timeout.Token);
        Assert.Equal(2, palworld.WorldActorSnapshotCallCount);
        await worker.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task RefreshRequestedDuringWorldFetchRunsAgainWithTheNewRevision()
    {
        var blockedWorld = new TaskCompletionSource<WorldActorSnapshotResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var palworld = new StubPalworldApiService { NextWorldActorSnapshot = blockedWorld };
        var collector = new RecordingCollectorApiService { WorldRevision = 1 };
        var refreshSignal = new GameDataRefreshSignal();
        var worker = new GameDataReportTimedBackgroundService(
            palworld,
            collector,
            refreshSignal,
            new StaticOptionsMonitor<CollectorSettings>(new()),
            new StubPalworldApiHealthService(),
            new RecordingCollectorDelay(),
            new CollectorMetrics(TimeProvider.System),
            NullLogger<GameDataReportTimedBackgroundService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(timeout.Token);
        await palworld.FirstWorldActorSnapshotCall.Task.WaitAsync(timeout.Token);
        collector.WorldRevision = 2;
        refreshSignal.Request(2);
        blockedWorld.TrySetResult(palworld.Snapshot);

        await collector.SecondWorldReport.Task.WaitAsync(timeout.Token);
        Assert.Equal([1, 2], collector.ReportedWorldRevisions);
        await worker.StopAsync(timeout.Token);
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
        var collector = new RecordingCollectorApiService();
        var worker = new PlayerLocationReporterTimedBackgroundService(
            palworld,
            collector,
            options,
            health,
            delay,
            new CollectorMetrics(TimeProvider.System),
            NullLogger<PlayerLocationReporterTimedBackgroundService>.Instance);

        await AssertScheduled(worker, delay, 77);

        Assert.Equal(1, health.MarkUnhealthyCallCount);
        Assert.Equal(
            (CollectorSourceSection.Players, CollectorSourceFailure.Unavailable),
            Assert.Single(collector.Failures));
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
            new CollectorMetrics(TimeProvider.System),
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
