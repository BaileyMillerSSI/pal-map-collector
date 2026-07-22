using System.Net;
using System.Net.Http.Headers;
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
    public void IngestOptionsBindThroughNormalConfigurationAndRequireExplicitDevelopmentHttp()
    {
        var development = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        AddValidIngestConfiguration(development.Configuration, "http://ingest.example.test/api/ingest/v1/snapshots");
        development.Configuration["PalmapIngest:AllowInsecureHttp"] = "true";
        development.AddCollectorApi();
        using var developmentHost = development.Build();

        var settings = developmentHost.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value;

        Assert.Equal(ValidClientId, settings.ClientId);
        Assert.IsType<SnapshotCollectorApiService>(developmentHost.Services.GetRequiredService<ICollectorApiService>());

        var production = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        AddValidIngestConfiguration(production.Configuration, "http://ingest.example.test/api/ingest/v1/snapshots");
        production.Configuration["PalmapIngest:AllowInsecureHttp"] = "true";
        production.AddCollectorApi();
        using var productionHost = production.Build();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = productionHost.Services.GetRequiredService<IOptions<PalmapIngestSettings>>().Value;
        });
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void PrivacyKeyMustBeExactly256Bits(string privacyKey)
    {
        var builder = Host.CreateApplicationBuilder();
        AddValidIngestConfiguration(builder.Configuration, "https://ingest.example.test/api/ingest/v1/snapshots");
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
            AddValidIngestConfiguration(
                configuration,
                "https://ingest.example.test/api/ingest/v1/snapshots");
            configuration["PalmapIngest:ClientId"] = clientId;
            configuration["PalmapIngest:ClientSecret"] = clientSecret;
        });
    }

    [Fact]
    public void IngestCredentialLengthsAreBounded()
    {
        AssertInvalidIngestConfiguration(configuration =>
        {
            AddValidIngestConfiguration(
                configuration,
                "https://ingest.example.test/api/ingest/v1/snapshots");
            configuration["PalmapIngest:ClientId"] = $"pmc_{new string('A', 15)}";
        });
        AssertInvalidIngestConfiguration(configuration =>
        {
            AddValidIngestConfiguration(
                configuration,
                "https://ingest.example.test/api/ingest/v1/snapshots");
            configuration["PalmapIngest:ClientId"] = $"pmc_{new string('A', 61)}";
        });
        AssertInvalidIngestConfiguration(configuration =>
        {
            AddValidIngestConfiguration(
                configuration,
                "https://ingest.example.test/api/ingest/v1/snapshots");
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
        AddValidIngestConfiguration(
            builder.Configuration,
            "https://ingest.example.test/api/ingest/v1/snapshots");
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
        var service = new SnapshotCollectorApiService(
            sanitizer,
            queue,
            TimeProvider.System,
            NullLogger<SnapshotCollectorApiService>.Instance);

        await service.ReportPlayerLocations(Players());
        await service.ReportGameData(World());
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
        AuthenticationHeaderValue? authorization = null;
        var handler = new AsyncHandler(async (request, cancellationToken) =>
        {
            authorization = request.Headers.Authorization;
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
        new HttpClientFactory(client),
        new StaticOptionsMonitor<PalmapIngestSettings>(ValidSettings()),
        TimeProvider.System,
        NullLogger<SnapshotDeliveryService>.Instance);

    private static void AddValidIngestConfiguration(IConfiguration configuration, string endpoint)
    {
        configuration["PalmapIngest:Endpoint"] = endpoint;
        configuration["PalmapIngest:ClientId"] = ValidClientId;
        configuration["PalmapIngest:ClientSecret"] = ValidClientSecret;
        configuration["PalmapIngest:PrivacyKey"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
    }

    private static PalmapIngestSettings ValidSettings() => new()
    {
        Endpoint = "https://ingest.example.test/api/ingest/v1/snapshots",
        ClientId = ValidClientId,
        ClientSecret = ValidClientSecret,
        PrivacyKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray())
    };

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

    private static WorldActorSnapshotResponse World() => new()
    {
        Time = "2026-07-21 12:00",
        Fps = 60,
        AverageFps = 59,
        ActorData =
        [
            new WorldActor { Type = "PalBox", GuildId = "raw-guild-id", GuildName = "Synthetic Guild", LocationX = 12_000, LocationY = -4_000, LocationZ = 0 },
            new WorldActor { Type = "Character", UnitType = "BaseCampPal", GuildId = "raw-guild-id", Level = 10, HitPoints = 100, MaxHitPoints = 120, LocationX = 12_010, LocationY = -3_990 },
            new WorldActor { Type = "Character", UnitType = "Player", UserId = "raw-user-id", GuildId = "raw-guild-id", GuildName = "Synthetic Guild", Stage = "Dungeon_Boss_Secret", IpAddress = "198.51.100.4" }
        ]
    };

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
