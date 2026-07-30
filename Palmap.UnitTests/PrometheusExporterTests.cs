using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Palmap.Collector.Configuration;
using Palmap.Collector.Metrics;
using Palmap.Collector.Services;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Metrics;
using Palmap.CollectorApi.Services;
using Palmap.CollectorApi.Services.Internal;
using Palmap.PalworldApi.Models;
using Palmap.Protocol;

namespace Palmap.UnitTests;

public sealed class PrometheusExporterTests
{
    private const string ValidClientId = "pmc_AAAAAAAAAAAAAAAAAAAA";
    private const string ValidClientSecret = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public async Task SamplerUpdatesCacheFromServerMetrics()
    {
        var palworld = new StubPalworldApiService();
        var cache = new PalworldMetricsCache();
        var sampler = CreateSampler(palworld, cache, new RecordingCollectorDelay());

        await sampler.SampleOnce(CancellationToken.None);

        Assert.Equal(1, palworld.ServerMetricsCallCount);
        Assert.True(cache.TryGet(out var metrics));
        Assert.Equal(palworld.Metrics.ServerFps, metrics.ServerFps);
        Assert.Equal(palworld.Metrics.CurrentPlayerCount, metrics.CurrentPlayerCount);
        Assert.Equal(palworld.Metrics.MaxPlayerCount, metrics.MaxPlayerCount);
    }

    [Fact]
    public async Task SamplerUsesSampleIntervalAfterSuccess()
    {
        var delay = new RecordingCollectorDelay();
        var sampler = CreateSampler(new StubPalworldApiService(), new PalworldMetricsCache(), delay, sampleIntervalMs: 42);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sampler.StartAsync(timeout.Token);
        Assert.Equal(42, await delay.ReadNext(timeout.Token));
        await sampler.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task SamplerDoesNotCallPalworldWhileHealthIsUnhealthy()
    {
        var palworld = new StubPalworldApiService();
        var health = new StubPalworldApiHealthService { IsHealthyResult = false };
        var sampler = CreateSampler(
            palworld,
            new PalworldMetricsCache(),
            new RecordingCollectorDelay(),
            health: health);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sampler.StartAsync(timeout.Token);
        await health.WaitStarted.WaitAsync(timeout.Token);

        Assert.Equal(0, palworld.ServerMetricsCallCount);
        await sampler.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task SamplerHttpFailureMarksUnhealthyAndUsesFailureRetryInterval()
    {
        var palworld = new StubPalworldApiService
        {
            ServerMetricsException = new HttpRequestException("unavailable")
        };
        var health = new StubPalworldApiHealthService();
        var delay = new RecordingCollectorDelay();
        var sampler = CreateSampler(
            palworld,
            new PalworldMetricsCache(),
            delay,
            failureRetryIntervalMs: 88,
            health: health);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sampler.StartAsync(timeout.Token);
        Assert.Equal(88, await delay.ReadNext(timeout.Token));
        Assert.Equal(1, health.MarkUnhealthyCallCount);
        await sampler.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task SnapshotMetricsExposeActorAndPlayerAggregates()
    {
        var (service, _, _) = CreateCollectorService();

        await service.ReportPlayerLocations(new PlayerListResponse
        {
            Players =
            [
                new PalworldPlayer
                {
                    Name = "One",
                    PlayerId = "player-1",
                    UserId = "user-1",
                    Ping = 10,
                    Level = 5,
                    BuildingCount = 2,
                    LocationX = 12_500,
                    LocationY = -4_200
                },
                new PalworldPlayer
                {
                    Name = "Two",
                    PlayerId = "player-2",
                    UserId = "user-2",
                    Ping = 30,
                    Level = 15,
                    BuildingCount = 8,
                    LocationX = 12_600,
                    LocationY = -4_100
                }
            ]
        });
        var revision = service.CaptureWorldRevision();
        await service.ReportGameData(
            new WorldActorSnapshotResponse
            {
                Time = "2026-07-21 12:00",
                Fps = 60,
                AverageFps = 55,
                InGameDays = 9,
                ActorData =
                [
                    new WorldActor { Type = "PalBox", GuildId = "guild-a", GuildName = "Guild", LocationX = 12_000, LocationY = -4_000, LocationZ = 0 },
                    new WorldActor { Type = "Character", UnitType = "Player", UserId = "user-1", GuildId = "guild-a" },
                    new WorldActor { Type = "Character", UnitType = "WildPal", Level = 3, HitPoints = 10, MaxHitPoints = 10 },
                    new WorldActor { Type = "Character", UnitType = "BaseCampPal", GuildId = "guild-a", Level = 4, HitPoints = 20, MaxHitPoints = 20, LocationX = 12_010, LocationY = -3_990 },
                    new WorldActor { Type = "Character", UnitType = "NPC" }
                ]
            },
            requestedRevision: revision);

        var snapshot = service.GetMetricsSnapshot();

        Assert.NotNull(snapshot.Players);
        Assert.Equal(2, snapshot.Players.Count);
        Assert.Equal(20, snapshot.Players.Average(player => player.PingMs));
        Assert.Equal(30, snapshot.Players.Max(player => player.PingMs));
        Assert.Equal(10, snapshot.Players.Average(player => player.Level));
        Assert.Equal(15, snapshot.Players.Max(player => player.Level));
        Assert.Equal(10, snapshot.Players.Sum(player => player.BuildingCount ?? 0));
        Assert.Equal(2, snapshot.Players.Count(player => player.Location.Kind == PlayerLocationKind.Overworld));

        Assert.NotNull(snapshot.World);
        Assert.Equal(60, snapshot.World.Stats.Fps);
        Assert.Equal(55, snapshot.World.Stats.AverageFps);
        Assert.Equal(9, snapshot.World.Stats.InGameDays);
        Assert.Equal(1, snapshot.World.Stats.ActorCounts.Players);
        Assert.Equal(1, snapshot.World.Stats.ActorCounts.WildPals);
        Assert.Equal(1, snapshot.World.Stats.ActorCounts.BasePals);
        Assert.Equal(1, snapshot.World.Stats.ActorCounts.Npcs);
        Assert.Equal(1, snapshot.World.Stats.ActorCounts.PalBoxes);
        Assert.Single(snapshot.World.Guilds);
        Assert.True(snapshot.Sequence >= 0);
        Assert.Equal(SnapshotSourceState.Healthy, snapshot.PlayersStatus.State);
        Assert.Equal(SnapshotSourceState.Healthy, snapshot.WorldStatus.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, "accepted")]
    [InlineData(HttpStatusCode.BadRequest, "rejected")]
    [InlineData(HttpStatusCode.InternalServerError, "retry")]
    [InlineData(HttpStatusCode.Unauthorized, "terminal")]
    public async Task DeliveryRecordsOutcomeCounters(HttpStatusCode statusCode, string expectedOutcome)
    {
        var collectorMetrics = new CollectorMetrics(TimeProvider.System);
        var outcomes = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "palmap_ingest_delivery_total")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome" && tag.Value is string outcome)
                {
                    outcomes.Add(outcome);
                }
            }
        });
        listener.Start();

        var handler = new AsyncHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        using var client = new HttpClient(handler);
        var service = new SnapshotDeliveryService(
            new LatestSnapshotQueue(),
            new NoOpHttpClientFactory(client),
            new StaticOptionsMonitor<PalmapIngestSettings>(ValidSettings()),
            TimeProvider.System,
            collectorMetrics,
            NullLogger<SnapshotDeliveryService>.Instance);

        await service.Send("{}"u8.ToArray(), CancellationToken.None);

        Assert.Contains(expectedOutcome, outcomes);
    }

    private static PalworldMetricsSampler CreateSampler(
        StubPalworldApiService palworld,
        PalworldMetricsCache cache,
        RecordingCollectorDelay delay,
        int sampleIntervalMs = 15_000,
        int failureRetryIntervalMs = 5_000,
        StubPalworldApiHealthService? health = null) =>
        new(
            palworld,
            cache,
            new StaticOptionsMonitor<PrometheusExporterSettings>(new PrometheusExporterSettings
            {
                Enabled = true,
                SampleIntervalMs = sampleIntervalMs
            }),
            new StaticOptionsMonitor<CollectorSettings>(new CollectorSettings
            {
                FailureRetryIntervalMs = failureRetryIntervalMs
            }),
            health ?? new StubPalworldApiHealthService(),
            delay,
            new CollectorMetrics(TimeProvider.System),
            NullLogger<PalworldMetricsSampler>.Instance);

    private static (SnapshotCollectorApiService Service, LatestSnapshotQueue Queue, GameDataRefreshSignal Signal)
        CreateCollectorService()
    {
        var queue = new LatestSnapshotQueue();
        var signal = new GameDataRefreshSignal();
        var service = new SnapshotCollectorApiService(
            new SnapshotSanitizer(new StaticOptionsMonitor<PalmapIngestSettings>(ValidSettings())),
            queue,
            signal,
            new StaticOptionsMonitor<CollectorSettings>(new()),
            TimeProvider.System,
            NullLogger<SnapshotCollectorApiService>.Instance);
        return (service, queue, signal);
    }

    private static PalmapIngestSettings ValidSettings() => new()
    {
        ClientId = ValidClientId,
        ClientSecret = ValidClientSecret,
        PrivacyKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray())
    };

    private sealed class NoOpHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
