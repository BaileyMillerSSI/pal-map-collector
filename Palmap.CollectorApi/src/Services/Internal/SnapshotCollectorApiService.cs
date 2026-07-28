using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Services;
using Palmap.PalworldApi.Models;
using Palmap.Protocol;

namespace Palmap.CollectorApi.Services.Internal;

internal sealed class SnapshotCollectorApiService(
    SnapshotSanitizer sanitizer,
    LatestSnapshotQueue queue,
    GameDataRefreshSignal gameDataRefreshSignal,
    IOptionsMonitor<CollectorSettings> collectorSettings,
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
    private readonly HashSet<string> _invalidSections = [];
    private HashSet<string> _stageRefreshPending = [];
    private long _stageRevision;
    private long _sequence;

    public Task ReportPlayerLocations(PlayerListResponse players, CancellationToken cancellationToken = default)
    {
        Update(
            () =>
            {
                var nextPlayers = sanitizer.Players(players);
                var previous = _players?.ToDictionary(player => player.Id, StringComparer.Ordinal)
                    ?? new Dictionary<string, SanitizedPlayer>(StringComparer.Ordinal);
                var online = nextPlayers.Select(player => player.Id).ToHashSet(StringComparer.Ordinal);
                var threshold = collectorSettings.CurrentValue.StageRefreshDistance;
                var thresholdSquared = (double)threshold * threshold;
                var refreshWorld = false;
                foreach (var player in nextPlayers)
                {
                    if (!previous.TryGetValue(player.Id, out var oldPlayer))
                    {
                        refreshWorld |= _stageRefreshPending.Add(player.Id);
                        continue;
                    }

                    var distanceSquared =
                        Math.Pow(player.X - oldPlayer.X, 2) +
                        Math.Pow(player.Y - oldPlayer.Y, 2);
                    if (distanceSquared >= thresholdSquared)
                    {
                        refreshWorld |= _stageRefreshPending.Add(player.Id);
                    }
                }

                _stageRefreshPending.RemoveWhere(id => !online.Contains(id));
                if (refreshWorld)
                {
                    gameDataRefreshSignal.Request(++_stageRevision);
                }

                _players = nextPlayers;
                _playersSlot = _playersSlot.Succeeded(timeProvider.GetUtcNow());
            },
            () => _playersSlot = _playersSlot.Failed(timeProvider.GetUtcNow(), SnapshotSourceState.Unavailable),
            "players");
        return Task.CompletedTask;
    }

    public long CaptureWorldRevision()
    {
        lock (_gate)
        {
            return _stageRevision;
        }
    }

    public Task ReportGameData(
        WorldActorSnapshotResponse snapshot,
        long requestedRevision,
        CancellationToken cancellationToken = default)
    {
        Update(
            () =>
            {
                _world = sanitizer.World(snapshot);
                if (requestedRevision == _stageRevision)
                {
                    _stageRefreshPending.Clear();
                }

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
                if (_invalidSections.Remove(section))
                {
                    logger.LogInformation(
                        "The {Section} source data recovered; fresh sanitized data is available again.",
                        section);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
            {
                fail();
                if (_invalidSections.Add(section))
                {
                    logger.LogWarning(
                        "Rejected invalid {Section} source data ({ExceptionType}); the related snapshot section " +
                        "may be stale. The collector will retry; update Palworld or the collector if this persists.",
                        section,
                        exception.GetType().Name);
                }
                else
                {
                    logger.LogDebug(
                        "Still rejecting invalid {Section} source data ({ExceptionType}); retained the last sanitized value.",
                        section,
                        exception.GetType().Name);
                }
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
