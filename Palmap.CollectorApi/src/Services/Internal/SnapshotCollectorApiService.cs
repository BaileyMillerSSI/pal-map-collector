using Microsoft.Extensions.Logging;
using Palmap.CollectorApi.Services;
using Palmap.PalworldApi.Models;
using Palmap.Protocol;

namespace Palmap.CollectorApi.Services.Internal;

internal sealed class SnapshotCollectorApiService(
    SnapshotSanitizer sanitizer,
    LatestSnapshotQueue queue,
    TimeProvider timeProvider,
    ILogger<SnapshotCollectorApiService> logger) : ICollectorApiService
{
    private readonly object _gate = new();
    private readonly Guid _collectorEpoch = Guid.NewGuid();
    private IReadOnlyList<SanitizedPlayer>? _players;
    private SanitizedWorld? _world;
    private PublicServerDetails? _server;
    private SourceSlot _playersSlot;
    private SourceSlot _worldSlot;
    private SourceSlot _serverSlot;
    private HashSet<string> _stageRefreshPending = [];
    private long _sequence;

    public Task ReportPlayerLocations(PlayerListResponse players, CancellationToken cancellationToken = default)
    {
        Update(
            () =>
            {
                _players = sanitizer.Players(players);
                _stageRefreshPending = _players.Select(player => player.Id).ToHashSet(StringComparer.Ordinal);
                _playersSlot = _playersSlot.Succeeded(timeProvider.GetUtcNow());
            },
            () => _playersSlot = _playersSlot.Failed(timeProvider.GetUtcNow(), SnapshotSourceState.Unavailable),
            "players");
        return Task.CompletedTask;
    }

    public Task ReportGameData(WorldActorSnapshotResponse snapshot, CancellationToken cancellationToken = default)
    {
        Update(
            () =>
            {
                _world = sanitizer.World(snapshot);
                _stageRefreshPending.Clear();
                _worldSlot = _worldSlot.Succeeded(timeProvider.GetUtcNow());
            },
            () => _worldSlot = _worldSlot.Failed(timeProvider.GetUtcNow(), SnapshotSourceState.Unavailable),
            "world");
        return Task.CompletedTask;
    }

    public Task ReportServerSettings(ServerSettingsResponse settings, CancellationToken cancellationToken = default)
    {
        Update(
            () =>
            {
                _server = sanitizer.Server(settings);
                _serverSlot = _serverSlot.Succeeded(timeProvider.GetUtcNow());
            },
            () => _serverSlot = _serverSlot.Failed(timeProvider.GetUtcNow(), SnapshotSourceState.Unavailable),
            "server");
        return Task.CompletedTask;
    }

    public Task ReportFailure(
        CollectorSourceSection section,
        CollectorSourceFailure failure,
        CancellationToken cancellationToken = default)
    {
        SnapshotEnvelopeV1 envelope;
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            var state = failure == CollectorSourceFailure.Unauthorized
                ? SnapshotSourceState.Unauthorized
                : SnapshotSourceState.Unavailable;
            switch (section)
            {
                case CollectorSourceSection.Players:
                    _playersSlot = _playersSlot.Failed(now, state);
                    break;
                case CollectorSourceSection.World:
                    _worldSlot = _worldSlot.Failed(now, state);
                    break;
                case CollectorSourceSection.Server:
                    _serverSlot = _serverSlot.Failed(now, state);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section), section, null);
            }

            envelope = CreateEnvelope(now);
            SnapshotContractV1.Validate(envelope);
        }

        queue.Publish(envelope);
        return Task.CompletedTask;
    }

    private void Update(Action update, Action fail, string section)
    {
        SnapshotEnvelopeV1 envelope;
        lock (_gate)
        {
            try
            {
                update();
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
            {
                fail();
                logger.LogWarning(
                    "Rejected an invalid {Section} response ({ExceptionType}); retained the last sanitized value.",
                    section,
                    exception.GetType().Name);
            }

            envelope = CreateEnvelope(timeProvider.GetUtcNow());
            SnapshotContractV1.Validate(envelope);
        }

        queue.Publish(envelope);
    }

    private SnapshotEnvelopeV1 CreateEnvelope(DateTimeOffset now)
    {
        var composition = sanitizer.Compose(_players, _world, _stageRefreshPending);
        return new SnapshotEnvelopeV1(
            SnapshotSchemaVersions.V1,
            typeof(SnapshotCollectorApiService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            now,
            _collectorEpoch,
            _sequence++,
            new LiveSnapshotV1(
                new SnapshotSection<IReadOnlyList<PublicPlayer>>(
                    _playersSlot.Status(_players is not null),
                    _players is null ? null : composition.Players),
                new SnapshotSection<PublicWorldData>(
                    _worldSlot.Status(_world is not null),
                    _world is null ? null : composition.World),
                new SnapshotSection<PublicServerDetails>(
                    _serverSlot.Status(_server is not null),
                    _server)));
    }

    private readonly record struct SourceSlot(
        SnapshotSourceState State,
        DateTimeOffset? LastAttemptedAt,
        DateTimeOffset? LastSuccessfulAt)
    {
        public SourceSlot Succeeded(DateTimeOffset now) =>
            new(SnapshotSourceState.Healthy, now, now);

        public SourceSlot Failed(DateTimeOffset now, SnapshotSourceState state) =>
            new(state, now, LastSuccessfulAt);

        public SourceStatus Status(bool hasData) => new(
            State,
            (State is SnapshotSourceState.Unauthorized or SnapshotSourceState.Unavailable) && hasData,
            LastAttemptedAt,
            LastSuccessfulAt);
    }
}
