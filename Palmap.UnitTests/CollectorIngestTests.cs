using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Palmap.Collector.Services;
using Palmap.CollectorApi;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Services;
using Palmap.CollectorApi.Services.Internal;
using Palmap.PalworldApi.Models;
using Palmap.Protocol;

namespace Palmap.UnitTests;

public sealed class CollectorIngestTests
{
    private const string ValidClientId = "pmc_AAAAAAAAAAAAAAAAAAAA";
    private const string ValidClientSecret = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void IngestOptionsDefaultToTheHostedPalMapEndpoint()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        AddValidIngestConfiguration(builder.Configuration);
        builder.AddCollectorApi();
        using var host = builder.Build();

        var settings = host.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value;

        Assert.Equal("https://pal-map.com", PalmapIngress.DefaultBaseUrl);
        Assert.Equal("https://pal-map.com/api/ingest/v1/snapshots", settings.Endpoint);
        Assert.Equal(ValidClientId, settings.ClientId);
        Assert.False(settings.SuppressIdleSnapshots);
        Assert.Equal(21_600_000, settings.IdleSnapshotHeartbeatIntervalMs);
        Assert.IsType<SnapshotCollectorApiService>(host.Services.GetRequiredService<ICollectorApiService>());
    }

    [Fact]
    public void IdleSnapshotHeartbeatIntervalMustBePositive()
    {
        var builder = Host.CreateApplicationBuilder();
        AddValidIngestConfiguration(builder.Configuration);
        builder.Configuration["PalmapIngest:IdleSnapshotHeartbeatIntervalMs"] = "0";
        builder.AddCollectorApi();
        using var host = builder.Build();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = host.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value;
        });
    }

    [Theory]
    [InlineData("Development", false, "https://ingest.example.test/api/ingest/v1/snapshots", false)]
    [InlineData("Development", true, "https://ingest.example.test/api/ingest/v1/snapshots", true)]
    [InlineData("Production", true, "https://ingest.example.test/api/ingest/v1/snapshots", false)]
    [InlineData("Development", false, "http://ingest.example.test/api/ingest/v1/snapshots", false)]
    [InlineData("Development", true, "http://ingest.example.test/api/ingest/v1/snapshots", true)]
    [InlineData("Production", true, "http://ingest.example.test/api/ingest/v1/snapshots", false)]
    public void IngestEndpointOverridesRequireExplicitDevelopmentInsecureMode(
        string environment,
        bool allowInsecureHttp,
        string endpoint,
        bool valid)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment
        });
        AddValidIngestConfiguration(builder.Configuration, endpoint);
        builder.Configuration["PalmapIngest:AllowInsecureHttp"] = allowInsecureHttp.ToString();
        builder.AddCollectorApi();
        using var host = builder.Build();

        if (valid)
        {
            Assert.Equal(endpoint, host.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value.Endpoint);
        }
        else
        {
            Assert.Throws<OptionsValidationException>(() =>
            {
                _ = host.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value;
            });
        }
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void PrivacyKeyMustBeExactly256Bits(string privacyKey)
    {
        var builder = Host.CreateApplicationBuilder();
        AddValidIngestConfiguration(builder.Configuration);
        builder.Configuration["PalmapIngest:PrivacyKey"] = privacyKey;
        builder.AddCollectorApi();
        using var host = builder.Build();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = host.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value;
        });
    }

    [Theory]
    [InlineData("https://user@ingest.example.test/api/ingest/v1/snapshots")]
    [InlineData("https://ingest.example.test/api/ingest/v1/snapshots?server=synthetic")]
    [InlineData("https://ingest.example.test/api/ingest/v1/snapshots#fragment")]
    public void IngestEndpointRejectsUserInfoQueryAndFragment(string endpoint)
    {
        AssertInvalidIngestConfiguration(configuration =>
            AddValidIngestConfiguration(configuration, endpoint));
    }

    [Theory]
    [InlineData("pmc_too-short", ValidClientSecret)]
    [InlineData("not_pmc_AAAAAAAAAAAAAAAAAAAA", ValidClientSecret)]
    [InlineData("pmc_AAAAAAAAAAAAAAAAAAA:", ValidClientSecret)]
    [InlineData(ValidClientId, "too-short")]
    [InlineData(ValidClientId, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB:")]
    [InlineData(ValidClientId, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=")]
    public void IngestCredentialsMustMatchHostedBasicFormat(string clientId, string clientSecret)
    {
        AssertInvalidIngestConfiguration(configuration =>
        {
            AddValidIngestConfiguration(configuration);
            configuration["PalmapIngest:ClientId"] = clientId;
            configuration["PalmapIngest:ClientSecret"] = clientSecret;
        });
    }

    [Fact]
    public void IngestCredentialLengthsAreBounded()
    {
        AssertInvalidIngestConfiguration(configuration =>
        {
            AddValidIngestConfiguration(configuration);
            configuration["PalmapIngest:ClientId"] = $"pmc_{new string('A', 15)}";
        });
        AssertInvalidIngestConfiguration(configuration =>
        {
            AddValidIngestConfiguration(configuration);
            configuration["PalmapIngest:ClientId"] = $"pmc_{new string('A', 61)}";
        });
        AssertInvalidIngestConfiguration(configuration =>
        {
            AddValidIngestConfiguration(configuration);
            configuration["PalmapIngest:ClientSecret"] = new string('B', 129);
        });
    }

    [Theory]
    [InlineData(16)]
    [InlineData(60)]
    public void IngestClientIdAcceptsHostedBoundaryLengths(int suffixLength)
    {
        var expectedClientId = $"pmc_{new string('A', suffixLength)}";
        var builder = Host.CreateApplicationBuilder();
        AddValidIngestConfiguration(builder.Configuration);
        builder.Configuration["PalmapIngest:ClientId"] = expectedClientId;
        builder.AddCollectorApi();
        using var host = builder.Build();

        var settings = host.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value;

        Assert.Equal(expectedClientId, settings.ClientId);
    }

    [Fact]
    public async Task CollectorBuildsFullAllowlistedSnapshotAndRetainsFailedSection()
    {
        var options = new StaticOptionsMonitor<PalmapIngestSettings>(ValidSettings());
        var sanitizer = new SnapshotSanitizer(options);
        var queue = new LatestSnapshotQueue();
        var refreshSignal = new GameDataRefreshSignal();
        var service = new SnapshotCollectorApiService(
            sanitizer,
            queue,
            refreshSignal,
            new StaticOptionsMonitor<CollectorSettings>(new()),
            TimeProvider.System,
            NullLogger<SnapshotCollectorApiService>.Instance);

        await service.ReportPlayerLocations(Players());
        await service.ReportGameData(World(), service.CaptureWorldRevision());
        await service.ReportServerSettings(Server());
        var complete = await queue.Read(CancellationToken.None);
        var json = SnapshotContractV1.Serialize(complete);

        Assert.Equal(SnapshotSourceState.Healthy, complete.Snapshot.Players.Status.State);
        Assert.Equal(SnapshotSourceState.Healthy, complete.Snapshot.World.Status.State);
        Assert.Equal(SnapshotSourceState.Healthy, complete.Snapshot.Server.Status.State);
        var player = Assert.Single(complete.Snapshot.Players.Data!);
        Assert.Equal(43, player.Id.Length);
        Assert.Equal(PlayerLocationKind.Instance, player.Location.Kind);
        Assert.Equal("instance", player.Location.Stage);
        Assert.Null(player.Location.X);
        var guild = Assert.Single(complete.Snapshot.World.Data!.Guilds);
        Assert.Equal(guild.Id, player.GuildId);
        Assert.Equal(1, Assert.Single(guild.Bases).PalCount);
        Assert.Null(complete.Snapshot.Server.Data!.Rules.MaxBuildings);
        Assert.DoesNotContain("raw-player-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-user-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-guild-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("198.51.100.4", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Dungeon_Boss_Secret", json, StringComparison.Ordinal);

        await service.ReportFailure(
            CollectorSourceSection.Players,
            CollectorSourceFailure.Unauthorized);
        var unauthorized = await queue.Read(CancellationToken.None);

        Assert.Equal(SnapshotSourceState.Unauthorized, unauthorized.Snapshot.Players.Status.State);
        Assert.True(unauthorized.Snapshot.Players.Status.IsStale);
        Assert.NotNull(unauthorized.Snapshot.Players.Status.LastSuccessfulAt);
        Assert.NotNull(unauthorized.Snapshot.Players.Data);

        await service.ReportPlayerLocations(Players() with
        {
            Players = [Players().Players[0] with { Ping = double.NaN }]
        });
        var retained = await queue.Read(CancellationToken.None);

        Assert.Equal(SnapshotSourceState.Unavailable, retained.Snapshot.Players.Status.State);
        Assert.True(retained.Snapshot.Players.Status.IsStale);
        Assert.NotNull(retained.Snapshot.Players.Data);
    }

    [Fact]
    public async Task StablePlayersRemainRenderableAndTeleportRefreshesAreDeduplicated()
    {
        var (service, queue, refreshSignal) = CreateCollectorService();

        await service.ReportPlayerLocations(PlayersAt(12_500, -4_200));
        var firstPlayers = await queue.Read(CancellationToken.None);
        Assert.Equal(PlayerLocationKind.Unknown, Assert.Single(firstPlayers.Snapshot.Players.Data!).Location.Kind);
        Assert.Equal(1, await refreshSignal.WaitForRevisionAfter(0, CancellationToken.None));

        await service.ReportGameData(World("None"), service.CaptureWorldRevision());
        _ = await queue.Read(CancellationToken.None);

        await service.ReportPlayerLocations(PlayersAt(12_510, -4_195));
        var ordinaryMovement = await queue.Read(CancellationToken.None);
        Assert.Equal(PlayerLocationKind.Overworld, Assert.Single(ordinaryMovement.Snapshot.Players.Data!).Location.Kind);
        Assert.Equal(1, service.CaptureWorldRevision());

        await service.ReportPlayerLocations(PlayersAt(62_510, -4_195));
        var teleport = await queue.Read(CancellationToken.None);
        Assert.Equal(PlayerLocationKind.Unknown, Assert.Single(teleport.Snapshot.Players.Data!).Location.Kind);
        Assert.Equal(2, service.CaptureWorldRevision());

        await service.ReportPlayerLocations(PlayersAt(112_510, -4_195));
        var repeatedTeleport = await queue.Read(CancellationToken.None);
        Assert.Equal(PlayerLocationKind.Unknown, Assert.Single(repeatedTeleport.Snapshot.Players.Data!).Location.Kind);
        Assert.Equal(2, service.CaptureWorldRevision());
        Assert.Equal(2, await refreshSignal.WaitForRevisionAfter(1, CancellationToken.None));
    }

    [Fact]
    public async Task InFlightWorldResponseCannotClearANewerPlayerTransition()
    {
        var (service, queue, refreshSignal) = CreateCollectorService();
        await service.ReportPlayerLocations(PlayersAt(12_500, -4_200));
        _ = await queue.Read(CancellationToken.None);
        _ = await refreshSignal.WaitForRevisionAfter(0, CancellationToken.None);
        await service.ReportGameData(World("None"), service.CaptureWorldRevision());
        _ = await queue.Read(CancellationToken.None);

        var inFlightRevision = service.CaptureWorldRevision();
        await service.ReportPlayerLocations(PlayersAt(62_500, -4_200));
        _ = await queue.Read(CancellationToken.None);
        var transitionRevision = service.CaptureWorldRevision();

        await service.ReportGameData(World("None"), inFlightRevision);
        var olderWorld = await queue.Read(CancellationToken.None);
        Assert.Equal(PlayerLocationKind.Unknown, Assert.Single(olderWorld.Snapshot.Players.Data!).Location.Kind);

        await service.ReportGameData(World("None"), transitionRevision);
        var refreshedWorld = await queue.Read(CancellationToken.None);
        Assert.Equal(PlayerLocationKind.Overworld, Assert.Single(refreshedWorld.Snapshot.Players.Data!).Location.Kind);
    }

    [Fact]
    public async Task OfflinePlayersArePrunedFromPendingRefreshes()
    {
        var (service, queue, refreshSignal) = CreateCollectorService();
        await service.ReportPlayerLocations(PlayersAt(12_500, -4_200));
        _ = await queue.Read(CancellationToken.None);
        _ = await refreshSignal.WaitForRevisionAfter(0, CancellationToken.None);
        await service.ReportGameData(World("None"), service.CaptureWorldRevision());
        _ = await queue.Read(CancellationToken.None);

        await service.ReportPlayerLocations(PlayersAt(62_500, -4_200));
        _ = await queue.Read(CancellationToken.None);
        Assert.Equal(2, service.CaptureWorldRevision());

        await service.ReportPlayerLocations(new PlayerListResponse { Players = [] });
        _ = await queue.Read(CancellationToken.None);
        await service.ReportPlayerLocations(PlayersAt(62_500, -4_200));
        var rejoined = await queue.Read(CancellationToken.None);

        Assert.Equal(3, service.CaptureWorldRevision());
        Assert.Equal(PlayerLocationKind.Unknown, Assert.Single(rejoined.Snapshot.Players.Data!).Location.Kind);
        Assert.Equal(3, await refreshSignal.WaitForRevisionAfter(1, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshSignalCoalescesToTheLatestRevision()
    {
        var signal = new GameDataRefreshSignal();
        signal.Request(2);
        signal.Request(3);
        signal.Request(4);

        Assert.Equal(4, await signal.WaitForRevisionAfter(1, CancellationToken.None));
    }

    [Fact]
    public async Task CollectorPopulatesExistingV1WorldPlayerAndServerFields()
    {
        var (service, queue, _) = CreateCollectorService();
        var first = Players().Players[0];
        await service.ReportPlayerLocations(new PlayerListResponse
        {
            Players =
            [
                first,
                first with
                {
                    Name = "Second Explorer",
                    PlayerId = "raw-player-id-2",
                    UserId = "raw-user-id-2",
                    BuildingCount = null,
                    LocationX = 13_000
                }
            ]
        });
        await service.ReportGameData(ParityWorld(), service.CaptureWorldRevision());
        await service.ReportServerSettings(FullServer());
        var envelope = await queue.Read(CancellationToken.None);

        Assert.Equal(2, envelope.Snapshot.Players.Data!.Count);
        Assert.Null(envelope.Snapshot.Players.Data[1].BuildingCount);
        Assert.All(envelope.Snapshot.Players.Data, player =>
            Assert.Equal(PlayerLocationKind.Overworld, player.Location.Kind));

        var world = envelope.Snapshot.World.Data!;
        Assert.Equal(42, world.Stats.InGameDays);
        Assert.Equal("14:30", world.Stats.InGameTime);
        var guild = Assert.Single(world.Guilds);
        Assert.Equal("Synthetic Guild", guild.Name);
        Assert.Equal(2, guild.OnlinePlayerCount);
        Assert.Equal(4, guild.KnownBuildingCount);
        Assert.False(guild.BuildingCountComplete);

        var server = envelope.Snapshot.Server.Data!;
        Assert.Equal(["Steam", "Xbox"], server.SupportedPlatforms);
        Assert.Equal(
            new PublicServerRules(
                "Custom", 2, 1.5, 0.8, 1.2, 1, 0.5, "Item",
                1.2, 0.75, 1.1, 0.9, 0.5, 0.7, 0.6, 0.8,
                2, 1.25, 0.5, 1.5, 0.7, 0, 10, 20, 0,
                false, true, true, true, true, false, 30, 1800),
            server.Rules);
    }

    [Fact]
    public void OmittedOptionalServerSettingsRemainUnknownAndLegacyPlatformsStillWork()
    {
        var sanitizer = new SnapshotSanitizer(
            new StaticOptionsMonitor<PalmapIngestSettings>(ValidSettings()));
        var minimal = sanitizer.Server(new ServerSettingsResponse
        {
            ServerName = "Minimal",
            ServerDescription = "Synthetic",
            ServerPlayerMaxNum = 32,
            BaseCampWorkerMaxNum = 15,
            DayTimeSpeedRate = 1,
            NightTimeSpeedRate = 1,
            AllowConnectPlatform = "(Steam, Xbox)"
        });

        Assert.Equal(["Steam", "Xbox"], minimal.SupportedPlatforms);
        foreach (var property in typeof(PublicServerRules).GetProperties())
        {
            Assert.Null(property.GetValue(minimal.Rules));
        }
    }

    [Fact]
    public void NumericInGameTimeIsNormalizedIntoTheExistingStringField()
    {
        var sanitizer = new SnapshotSanitizer(
            new StaticOptionsMonitor<PalmapIngestSettings>(ValidSettings()));
        var world = sanitizer.World(ParityWorld() with { InGameTime = JsonValue("930.5") });

        Assert.Equal("930.5", world.Stats.InGameTime);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "Unauthorized")]
    [InlineData(HttpStatusCode.InternalServerError, "Unavailable")]
    public void ReporterClassifiesOnlyPalworldAuthenticationFailuresAsUnauthorized(
        HttpStatusCode status,
        string expected)
    {
        var exception = new HttpRequestException("Synthetic failure.", null, status);

        Assert.Equal(expected, TimedReporterBackgroundService.ClassifySourceFailure(exception).ToString());
    }

    [Fact]
    public async Task QueueKeepsOnlyLatestEnvelopeBetweenDeliveryAttempts()
    {
        var queue = new LatestSnapshotQueue();
        var fixture = SnapshotContractV1.Deserialize(File.ReadAllBytes(FixturePath()));
        queue.Publish(fixture with { Sequence = 1 });
        queue.Publish(fixture with { Sequence = 2 });
        queue.Publish(fixture with { Sequence = 3 });

        Assert.Equal(3, (await queue.Read(CancellationToken.None)).Sequence);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, "Accepted")]
    [InlineData(HttpStatusCode.Unauthorized, "Terminal")]
    [InlineData(HttpStatusCode.UpgradeRequired, "Terminal")]
    [InlineData(HttpStatusCode.TooManyRequests, "Retry")]
    [InlineData(HttpStatusCode.InternalServerError, "Retry")]
    [InlineData(HttpStatusCode.BadRequest, "Rejected")]
    public void DeliveryClassifiesStatusWithoutReadingUpstreamBody(HttpStatusCode status, string expected)
    {
        var result = SnapshotDeliveryService.Classify(
            status,
            null,
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromMinutes(1));

        Assert.Equal(expected, result.Outcome.ToString());
    }

    [Fact]
    public void RetryAfterIsBounded()
    {
        var result = SnapshotDeliveryService.Classify(
            HttpStatusCode.TooManyRequests,
            new RetryConditionHeaderValue(TimeSpan.FromHours(1)),
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromMinutes(1), result.RetryAfter);
    }

    [Fact]
    public async Task SendUsesBasicAuthStableBytesAndTreatsTimeoutAsRetry()
    {
        byte[]? captured = null;
        Uri? requestUri = null;
        AuthenticationHeaderValue? authorization = null;
        var handler = new AsyncHandler(async (request, cancellationToken) =>
        {
            authorization = request.Headers.Authorization;
            requestUri = request.RequestUri;
            captured = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = new HttpClient(handler);
        var service = DeliveryService(client);
        var stable = "{\"sequence\":7}"u8.ToArray();

        var accepted = await service.Send(stable, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Accepted, accepted.Outcome);
        Assert.Equal(stable, captured);
        Assert.Equal("Basic", authorization?.Scheme);
        Assert.Equal(new Uri(PalmapIngestSettings.DefaultEndpoint), requestUri);
        Assert.Equal(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{ValidClientId}:{ValidClientSecret}")),
            authorization?.Parameter);

        using var timedOutClient = new HttpClient(new AsyncHandler((_, _) => throw new TaskCanceledException()));
        var timedOut = await DeliveryService(timedOutClient).Send(stable, CancellationToken.None);
        Assert.Equal(DeliveryOutcome.Retry, timedOut.Outcome);
    }

    [Fact]
    public void MapProjectionUsesReleasedBoundsAndWorldTreePrecedence()
    {
        Assert.Equal(MapLayerId.WorldTree, MapProjection.Classify(348_000, -500_000));
        Assert.Equal(MapLayerId.Palpagos, MapProjection.Classify(1, 2));
        Assert.Null(MapProjection.Classify(2_000_000, 2_000_000));
    }

    private static SnapshotDeliveryService DeliveryService(HttpClient client) => new(
        new LatestSnapshotQueue(),
        new IdleSnapshotPolicy(TimeProvider.System),
        new HttpClientFactory(client),
        new StaticOptionsMonitor<PalmapIngestSettings>(ValidSettings()),
        TimeProvider.System,
        NullLogger<SnapshotDeliveryService>.Instance);

    private static void AddValidIngestConfiguration(IConfiguration configuration, string? endpoint = null)
    {
        if (endpoint is not null)
        {
            configuration["PalmapIngest:Endpoint"] = endpoint;
        }

        configuration["PalmapIngest:ClientId"] = ValidClientId;
        configuration["PalmapIngest:ClientSecret"] = ValidClientSecret;
        configuration["PalmapIngest:PrivacyKey"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
    }

    private static PalmapIngestSettings ValidSettings() => new()
    {
        ClientId = ValidClientId,
        ClientSecret = ValidClientSecret,
        PrivacyKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray())
    };

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

    private static PlayerListResponse Players() => new()
    {
        Players = [new PalworldPlayer
        {
            Name = "Explorer 198.51.100.4",
            AccountName = "raw-account-id",
            PlayerId = "raw-player-id",
            UserId = "raw-user-id",
            IpAddress = "198.51.100.4",
            Ping = 12.6,
            LocationX = 12_500,
            LocationY = -4_200,
            Level = 10,
            BuildingCount = 4
        }]
    };

    private static PlayerListResponse PlayersAt(double x, double y) => Players() with
    {
        Players = [Players().Players[0] with { LocationX = x, LocationY = y }]
    };

    private static WorldActorSnapshotResponse World(string stage = "Dungeon_Boss_Secret") => new()
    {
        Time = "2026-07-21 12:00",
        Fps = 60,
        AverageFps = 59,
        ActorData =
        [
            new WorldActor { Type = "PalBox", GuildId = "raw-guild-id", GuildName = "Synthetic Guild", LocationX = 12_000, LocationY = -4_000, LocationZ = 0 },
            new WorldActor { Type = "Character", UnitType = "BaseCampPal", GuildId = "raw-guild-id", Level = 10, HitPoints = 100, MaxHitPoints = 120, LocationX = 12_010, LocationY = -3_990 },
            new WorldActor { Type = "Character", UnitType = "Player", UserId = "raw-user-id", GuildId = "raw-guild-id", GuildName = "Synthetic Guild", Stage = stage, IpAddress = "198.51.100.4" }
        ]
    };

    private static WorldActorSnapshotResponse ParityWorld() => World("None") with
    {
        InGameDays = 42.9,
        InGameTime = JsonValue("\"14:30\""),
        ActorData =
        [
            new WorldActor { Type = "PalBox", GuildId = "raw-guild-id", LocationX = 12_000, LocationY = -4_000, LocationZ = 0 },
            new WorldActor { Type = "Character", UnitType = "BaseCampPal", GuildId = "raw-guild-id", Level = 10, HitPoints = 100, MaxHitPoints = 120, LocationX = 12_010, LocationY = -3_990 },
            new WorldActor { Type = "Character", UnitType = "Player", UserId = "raw-user-id", GuildId = "raw-guild-id", GuildName = "Synthetic Guild", Stage = "None" },
            new WorldActor { Type = "Character", UnitType = "Player", UserId = "raw-user-id-2", GuildId = "raw-guild-id", GuildName = "Synthetic Guild", Stage = "None" }
        ]
    };

    private static ServerSettingsResponse FullServer() => Server() with
    {
        Difficulty = "Custom",
        ExpRate = 2,
        PalCaptureRate = 1.5,
        PalSpawnNumRate = 0.8,
        WorkSpeedRate = 1.2,
        PalEggDefaultHatchingTime = 1,
        ItemWeightRate = 0.5,
        DeathPenalty = "Item",
        PlayerDamageRateAttack = 1.2,
        PlayerDamageRateDefense = 0.75,
        PalDamageRateAttack = 1.1,
        PalDamageRateDefense = 0.9,
        PlayerStomachDecreaceRate = 0.5,
        PlayerStaminaDecreaceRate = 0.7,
        PalStomachDecreaceRate = 0.6,
        PalStaminaDecreaceRate = 0.8,
        CollectionDropRate = 2,
        CollectionObjectHpRate = 1.25,
        CollectionObjectRespawnSpeedRate = 0.5,
        EnemyDropItemRate = 1.5,
        BuildObjectDamageRate = 0.7,
        BuildObjectDeteriorationDamageRate = 0,
        BaseCampMaxNum = 99,
        BaseCampMaxNumInGuild = 10,
        GuildPlayerMaxNum = 20,
        MaxBuildingLimitNum = 0,
        Hardcore = false,
        EnableFastTravel = true,
        EnableInvaderEnemy = true,
        AllowClientMod = true,
        IsUseBackupSaveData = true,
        EnableVoiceChat = false,
        AutoSaveSpan = 30,
        SupplyDropSpan = 1800,
        CrossplayPlatforms = ["Steam", "Xbox", "Steam"],
        AllowConnectPlatform = "(Legacy)"
    };

    private static JsonElement JsonValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ServerSettingsResponse Server() => new()
    {
        ServerName = "Synthetic",
        ServerDescription = "Join 198.51.100.4",
        ServerPlayerMaxNum = 32,
        DropItemMaxNum = 777,
        BaseCampWorkerMaxNum = 15,
        BaseCampMaxNum = 3,
        GuildPlayerMaxNum = 20,
        DayTimeSpeedRate = 1,
        NightTimeSpeedRate = 1,
        PublicIp = "198.51.100.4",
        PublicPort = 8211,
        AllowConnectPlatform = "(Steam, Xbox)",
        ExpRate = 1,
        PalCaptureRate = 1,
        PalSpawnNumRate = 1
    };

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "snapshot-v1.synthetic.json");

    private static void AssertInvalidIngestConfiguration(Action<IConfiguration> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        configure(builder.Configuration);
        builder.AddCollectorApi();
        using var host = builder.Build();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = host.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value;
        });
    }

    private sealed class HttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class AsyncHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
