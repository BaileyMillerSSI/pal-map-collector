using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Services.Internal;
using Palmap.Protocol;

namespace Palmap.UnitTests;

public sealed class IdleSnapshotDeliveryTests
{
    [Fact]
    public async Task SuppressionIsDisabledByDefault()
    {
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.Accepted, HttpStatusCode.Accepted]);
        var (service, handler, _) = DeliveryService(Settings(suppress: false), responses);
        var baseline = HealthyEmpty();

        await service.ProcessSnapshot(baseline, CancellationToken.None);
        await service.ProcessSnapshot(RoutineChurn(baseline), CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task PendingOrIncompleteStartupSnapshotsNeverArmSuppression()
    {
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.Accepted, HttpStatusCode.Accepted]);
        var (service, handler, _) = DeliveryService(Settings(), responses);
        var pending = HealthyEmpty() with
        {
            Snapshot = HealthyEmpty().Snapshot with
            {
                World = new SnapshotSection<PublicWorldData>(
                    new SourceStatus(SnapshotSourceState.Pending, false, null, null),
                    null)
            }
        };

        await service.ProcessSnapshot(pending, CancellationToken.None);
        await service.ProcessSnapshot(pending with { Sequence = pending.Sequence + 1 }, CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task AcceptedHealthyEmptyBaselineSuppressesRoutineSemanticChurn()
    {
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.Accepted]);
        var (service, handler, _) = DeliveryService(Settings(), responses);
        var baseline = HealthyEmpty();

        await service.ProcessSnapshot(baseline, CancellationToken.None);
        await service.ProcessSnapshot(RoutineChurn(baseline), CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RejectionAndTransientFailureNeverArmSuppression(HttpStatusCode failure)
    {
        var responses = new Queue<HttpStatusCode>([failure, HttpStatusCode.Accepted]);
        var (service, handler, _) = DeliveryService(Settings(), responses);
        var baseline = HealthyEmpty();

        await service.ProcessSnapshot(baseline, CancellationToken.None);
        await service.ProcessSnapshot(RoutineChurn(baseline), CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ClientTimeoutNeverArmsSuppression()
    {
        var settings = Settings();
        var monitor = new StaticOptionsMonitor<PalmapIngestSettings>(settings);
        var handler = new TimeoutThenAcceptedHandler();
        using var client = new HttpClient(handler);
        var service = new SnapshotDeliveryService(
            new LatestSnapshotQueue(),
            new IdleSnapshotPolicy(monitor, TimeProvider.System),
            new HttpClientFactory(client),
            monitor,
            TimeProvider.System,
            NullLogger<SnapshotDeliveryService>.Instance);
        var baseline = HealthyEmpty();

        await service.ProcessSnapshot(baseline, CancellationToken.None);
        await service.ProcessSnapshot(RoutineChurn(baseline), CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task FirstPlayerResumesDeliveryImmediately()
    {
        var responses = new Queue<HttpStatusCode>(
            [HttpStatusCode.Accepted, HttpStatusCode.Accepted]);
        var (service, handler, _) = DeliveryService(Settings(), responses);
        var baseline = HealthyEmpty();
        await service.ProcessSnapshot(baseline, CancellationToken.None);

        var withPlayer = baseline with
        {
            Sequence = baseline.Sequence + 1,
            Snapshot = baseline.Snapshot with
            {
                Players = baseline.Snapshot.Players with
                {
                    Data = [SyntheticPlayer()]
                }
            }
        };
        await service.ProcessSnapshot(withPlayer, CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData("players")]
    [InlineData("world")]
    [InlineData("server")]
    public async Task EverySourceDegradationAndRecoveryResumesDelivery(string section)
    {
        var responses = new Queue<HttpStatusCode>(
            [HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.Accepted]);
        var (service, handler, _) = DeliveryService(Settings(), responses);
        var baseline = HealthyEmpty();
        await service.ProcessSnapshot(baseline, CancellationToken.None);

        var degradedStatus = new SourceStatus(
            SnapshotSourceState.Unavailable,
            true,
            baseline.CollectedAt.AddMinutes(1),
            baseline.CollectedAt);
        var degradedSnapshot = section switch
        {
            "players" => baseline.Snapshot with
            {
                Players = baseline.Snapshot.Players with
                {
                    Status = degradedStatus
                }
            },
            "world" => baseline.Snapshot with
            {
                World = baseline.Snapshot.World with { Status = degradedStatus }
            },
            "server" => baseline.Snapshot with
            {
                Server = baseline.Snapshot.Server with { Status = degradedStatus }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
        };
        var degraded = baseline with
        {
            Sequence = baseline.Sequence + 1,
            Snapshot = degradedSnapshot
        };
        await service.ProcessSnapshot(degraded, CancellationToken.None);

        var recovered = baseline with { Sequence = baseline.Sequence + 2 };
        await service.ProcessSnapshot(recovered, CancellationToken.None);
        await service.ProcessSnapshot(RoutineChurn(recovered), CancellationToken.None);

        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task ChangedPublicServerConfigurationBreaksSilence()
    {
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.Accepted, HttpStatusCode.Accepted]);
        var (service, handler, _) = DeliveryService(Settings(), responses);
        var baseline = HealthyEmpty();
        await service.ProcessSnapshot(baseline, CancellationToken.None);

        var changed = baseline with
        {
            Sequence = baseline.Sequence + 1,
            Snapshot = baseline.Snapshot with
            {
                Server = baseline.Snapshot.Server with
                {
                    Data = baseline.Snapshot.Server.Data! with { MaxPlayers = 64 }
                }
            }
        };
        await service.ProcessSnapshot(changed, CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ChangedPrivacyIdentityConfigurationBreaksSilence()
    {
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.Accepted, HttpStatusCode.Accepted]);
        var monitor = new MutableOptionsMonitor<PalmapIngestSettings>(Settings());
        var time = new ManualTimeProvider(HealthyEmpty().CollectedAt);
        var handler = new QueueResponseHandler(responses);
        using var client = new HttpClient(handler);
        var service = new SnapshotDeliveryService(
            new LatestSnapshotQueue(),
            new IdleSnapshotPolicy(monitor, time),
            new HttpClientFactory(client),
            monitor,
            time,
            NullLogger<SnapshotDeliveryService>.Instance);
        var baseline = HealthyEmpty();
        await service.ProcessSnapshot(baseline, CancellationToken.None);

        monitor.Value = monitor.Value with
        {
            PrivacyKey = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray())
        };
        await service.ProcessSnapshot(RoutineChurn(baseline), CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task HeartbeatDeadlineDisarmsUntilTheDueBaselineIsAccepted()
    {
        var responses = new Queue<HttpStatusCode>(
            [HttpStatusCode.Accepted, HttpStatusCode.InternalServerError, HttpStatusCode.Accepted]);
        var (service, handler, time) = DeliveryService(Settings(heartbeatMs: 21_600_000), responses);
        var baseline = HealthyEmpty();
        await service.ProcessSnapshot(baseline, CancellationToken.None);

        time.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMilliseconds(1));
        await service.ProcessSnapshot(RoutineChurn(baseline), CancellationToken.None);
        Assert.Equal(1, handler.RequestCount);

        time.Advance(TimeSpan.FromMilliseconds(1));
        var due = baseline with { Sequence = baseline.Sequence + 2 };
        await service.ProcessSnapshot(due, CancellationToken.None);
        await service.ProcessSnapshot(due with { Sequence = due.Sequence + 1 }, CancellationToken.None);
        await service.ProcessSnapshot(due with { Sequence = due.Sequence + 2 }, CancellationToken.None);

        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task NewProcessRequiresANewAcceptedBaseline()
    {
        var baseline = HealthyEmpty();
        var firstResponses = new Queue<HttpStatusCode>([HttpStatusCode.Accepted]);
        var (first, firstHandler, _) = DeliveryService(Settings(), firstResponses);
        await first.ProcessSnapshot(baseline, CancellationToken.None);
        await first.ProcessSnapshot(RoutineChurn(baseline), CancellationToken.None);
        Assert.Equal(1, firstHandler.RequestCount);

        var restartedResponses = new Queue<HttpStatusCode>([HttpStatusCode.Accepted]);
        var (restarted, restartedHandler, _) = DeliveryService(Settings(), restartedResponses);
        await restarted.ProcessSnapshot(baseline with { CollectorEpoch = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(1, restartedHandler.RequestCount);
    }

    private static (SnapshotDeliveryService Service, QueueResponseHandler Handler, ManualTimeProvider Time)
        DeliveryService(PalmapIngestSettings settings, Queue<HttpStatusCode> responses)
    {
        var monitor = new StaticOptionsMonitor<PalmapIngestSettings>(settings);
        var time = new ManualTimeProvider(HealthyEmpty().CollectedAt);
        var handler = new QueueResponseHandler(responses);
        var client = new HttpClient(handler);
        return (
            new SnapshotDeliveryService(
                new LatestSnapshotQueue(),
                new IdleSnapshotPolicy(monitor, time),
                new HttpClientFactory(client),
                monitor,
                time,
                NullLogger<SnapshotDeliveryService>.Instance),
            handler,
            time);
    }

    private static PalmapIngestSettings Settings(
        bool suppress = true,
        int heartbeatMs = 21_600_000) => new()
        {
            ClientId = "pmc_AAAAAAAAAAAAAAAAAAAA",
            ClientSecret = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            PrivacyKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            SuppressIdleSnapshots = suppress,
            IdleSnapshotHeartbeatIntervalMs = heartbeatMs,
            MaximumDeliveryAttempts = 1
        };

    private static SnapshotEnvelopeV1 HealthyEmpty()
    {
        var fixture = SnapshotContractV1.Deserialize(File.ReadAllBytes(FixturePath()));
        var now = fixture.CollectedAt;
        var healthy = new SourceStatus(SnapshotSourceState.Healthy, false, now, now);
        return fixture with
        {
            Snapshot = fixture.Snapshot with
            {
                Players = new SnapshotSection<IReadOnlyList<PublicPlayer>>(healthy, []),
                World = fixture.Snapshot.World with { Status = healthy },
                Server = fixture.Snapshot.Server with { Status = healthy }
            }
        };
    }

    private static SnapshotEnvelopeV1 RoutineChurn(SnapshotEnvelopeV1 baseline)
    {
        var collectedAt = baseline.CollectedAt.AddMinutes(1);
        var healthy = new SourceStatus(SnapshotSourceState.Healthy, false, collectedAt, collectedAt);
        var world = baseline.Snapshot.World.Data!;
        return baseline with
        {
            Sequence = baseline.Sequence + 1,
            CollectedAt = collectedAt,
            Snapshot = baseline.Snapshot with
            {
                Players = baseline.Snapshot.Players with { Status = healthy },
                World = baseline.Snapshot.World with
                {
                    Status = healthy,
                    Data = world with
                    {
                        Stats = world.Stats with
                        {
                            SourceTime = "2026-08-03 13:00",
                            Fps = world.Stats.Fps + 1
                        }
                    }
                },
                Server = baseline.Snapshot.Server with
                {
                    Status = healthy,
                    Data = baseline.Snapshot.Server.Data! with
                    {
                        SupportedPlatforms = baseline.Snapshot.Server.Data!.SupportedPlatforms.ToArray()
                    }
                }
            }
        };
    }

    private static PublicPlayer SyntheticPlayer() => new(
        "AAAAAAAAAAAAAAAA",
        "Synthetic Explorer",
        1,
        10,
        null,
        null,
        new PlayerLocation(PlayerLocationKind.Overworld, MapLayerId.Palpagos, 1, 2, null));

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "snapshot-v1.synthetic.json");

    private sealed class HttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class QueueResponseHandler(Queue<HttpStatusCode> responses) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(responses.Dequeue()));
        }
    }

    private sealed class TimeoutThenAcceptedHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return RequestCount == 1
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException())
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }

    private sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T Value { get; set; } = value;

        public T CurrentValue => Value;

        public T Get(string? name) => Value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
