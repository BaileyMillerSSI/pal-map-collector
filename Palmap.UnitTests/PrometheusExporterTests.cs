using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
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
    [InlineData("14:30", 870)]
    [InlineData("0:00", 0)]
    [InlineData("23:59", 1439)]
    [InlineData("9:05", 545)]
    public void ParsesInGameTimeMinutes(string time, long expectedMinutes)
    {
        Assert.True(CollectorObservableMetrics.TryParseInGameTimeMinutes(time, out var minutes));
        Assert.Equal(expectedMinutes, minutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    public void RejectsInvalidInGameTime(string? time)
    {
        Assert.False(CollectorObservableMetrics.TryParseInGameTimeMinutes(time, out _));
    }

    [Fact]
    public void ServerRuleRatesAndFlagsOmitNullValues()
    {
        var empty = EmptyRules();
        Assert.Empty(CollectorObservableMetrics.ServerRuleRates(empty));
        Assert.Empty(CollectorObservableMetrics.ServerRuleEnabled(empty));

        var rules = new PublicServerRules(
            "Custom", 2, 1.5, null, null, null, null, "Item",
            null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null,
            false, true, null, null, null, null, null, null);

        var rates = CollectorObservableMetrics.ServerRuleRates(rules).ToArray();
        Assert.Contains(rates, m => TagEquals(m, "rule", "experience") && m.Value == 2);
        Assert.Contains(rates, m => TagEquals(m, "rule", "pal_capture") && m.Value == 1.5);
        Assert.DoesNotContain(rates, m => TagEquals(m, "rule", "pal_spawn"));

        var flags = CollectorObservableMetrics.ServerRuleEnabled(rules).ToArray();
        Assert.Contains(flags, m => TagEquals(m, "rule", "hardcore") && m.Value == 0);
        Assert.Contains(flags, m => TagEquals(m, "rule", "fast_travel") && m.Value == 1);
        Assert.DoesNotContain(flags, m => TagEquals(m, "rule", "voice_chat"));
    }

    [Fact]
    public async Task SnapshotMetricsExposeGuildServerAndInGameTimeAggregates()
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
                InGameTime = JsonDocument.Parse("\"14:30\"").RootElement.Clone(),
                ActorData =
                [
                    new WorldActor { Type = "PalBox", GuildId = "guild-a", GuildName = "Guild Alpha", LocationX = 12_000, LocationY = -4_000, LocationZ = 0 },
                    new WorldActor { Type = "Character", UnitType = "Player", UserId = "user-1", GuildId = "guild-a", GuildName = "Guild Alpha" },
                    new WorldActor
                    {
                        Type = "Character",
                        UnitType = "BaseCampPal",
                        GuildId = "guild-a",
                        Level = 4,
                        HitPoints = 20,
                        MaxHitPoints = 40,
                        LocationX = 12_010,
                        LocationY = -3_990
                    }
                ]
            },
            requestedRevision: revision);
        await service.ReportServerSettings(new ServerSettingsResponse
        {
            ServerName = "Synthetic",
            ServerDescription = "Test",
            ServerPlayerMaxNum = 32,
            BaseCampWorkerMaxNum = 15,
            DayTimeSpeedRate = 1.5,
            NightTimeSpeedRate = 0.5,
            ExpRate = 2,
            PalCaptureRate = 1.25,
            DeathPenalty = "Item",
            Hardcore = false,
            EnableFastTravel = true,
            EnableVoiceChat = false
        });

        var snapshot = service.GetMetricsSnapshot();

        Assert.NotNull(snapshot.Players);
        Assert.Single(snapshot.Players);
        Assert.NotNull(snapshot.World);
        Assert.Equal("14:30", snapshot.World.Stats.InGameTime);
        Assert.True(CollectorObservableMetrics.TryParseInGameTimeMinutes(snapshot.World.Stats.InGameTime, out var minutes));
        Assert.Equal(870, minutes);
        var guild = Assert.Single(snapshot.World.Guilds);
        Assert.Equal("Guild Alpha", guild.Name);
        Assert.Equal(1, guild.BaseCount);
        Assert.Equal(1, guild.BasePalCount);
        Assert.Equal(1, guild.OnlinePlayerCount);
        Assert.Equal(1, snapshot.World.Guilds.Sum(g => g.BaseCount));

        Assert.NotNull(snapshot.Server);
        Assert.Equal(32, snapshot.Server.MaxPlayers);
        Assert.Equal(15, snapshot.Server.MaxPalsPerBase);
        Assert.Equal(1.5, snapshot.Server.DayTimeSpeedRate);
        Assert.Equal(0.5, snapshot.Server.NightTimeSpeedRate);
        Assert.Equal(2, snapshot.Server.Rules.ExperienceRate);
        Assert.Equal(1.25, snapshot.Server.Rules.PalCaptureRate);
        Assert.Equal("Item", snapshot.Server.Rules.DeathPenalty);
        Assert.False(snapshot.Server.Rules.HardcoreEnabled);
        Assert.True(snapshot.Server.Rules.FastTravelEnabled);
        Assert.False(snapshot.Server.Rules.VoiceChatEnabled);

        var rates = CollectorObservableMetrics.ServerRuleRates(snapshot.Server.Rules).ToArray();
        Assert.Contains(rates, m => TagEquals(m, "rule", "experience") && m.Value == 2);
        var flags = CollectorObservableMetrics.ServerRuleEnabled(snapshot.Server.Rules).ToArray();
        Assert.Contains(flags, m => TagEquals(m, "rule", "fast_travel") && m.Value == 1);
    }

    [Fact]
    public void EmptySnapshotSectionsYieldNoWorldOrServerAggregates()
    {
        var empty = new CollectorMetricsSnapshot(
            Sequence: 0,
            StageRefreshPendingCount: 0,
            PlayersStatus: new SourceStatus(SnapshotSourceState.Pending, false, null, null),
            WorldStatus: new SourceStatus(SnapshotSourceState.Pending, false, null, null),
            ServerStatus: new SourceStatus(SnapshotSourceState.Pending, false, null, null),
            Players: null,
            World: null,
            Server: null);

        Assert.Null(empty.Players);
        Assert.Null(empty.World);
        Assert.Null(empty.Server);
        Assert.Empty(CollectorObservableMetrics.ServerRuleRates(EmptyRules()));
        Assert.Empty(CollectorObservableMetrics.ServerRuleEnabled(EmptyRules()));
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

    private static PublicServerRules EmptyRules() => new(
        null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null);

    private static bool TagEquals<T>(Measurement<T> measurement, string key, string expected)
        where T : struct =>
        measurement.Tags.ToArray().Any(tag => tag.Key == key && Equals(tag.Value, expected));

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
