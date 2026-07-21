using System.Threading.Channels;
using Palmap.Protocol;

namespace Palmap.Collector;

internal enum SourceKind { Players, World, Settings }

internal sealed class SnapshotState(PrivacyTransformer transformer, LatestSnapshotQueue queue, TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private readonly Guid _epoch = Guid.NewGuid();
    private IReadOnlyList<SanitizedPlayer>? _players;
    private SanitizedWorldProjection? _world;
    private PublicServerDetails? _server;
    private SectionStatus _playersStatus;
    private SectionStatus _worldStatus;
    private SectionStatus _serverStatus;
    private HashSet<string> _stageRefreshPending = [];
    private long _sequence;

    public void PlayersSucceeded(PalworldPlayerList value) => Mutate(() =>
    {
        _players = transformer.SanitizePlayers(value);
        _stageRefreshPending = _players.Select(player => player.Id).ToHashSet(StringComparer.Ordinal);
        _playersStatus = _playersStatus.Succeeded(timeProvider.GetUtcNow());
    });

    public void WorldSucceeded(PalworldWorldSnapshot world) => Mutate(() =>
    {
        _world = transformer.SanitizeWorld(world);
        _stageRefreshPending.Clear();
        _worldStatus = _worldStatus.Succeeded(timeProvider.GetUtcNow());
    });

    public void SettingsSucceeded(PalworldSettings settings) => Mutate(() =>
    {
        _server = transformer.SanitizeSettings(settings);
        _serverStatus = _serverStatus.Succeeded(timeProvider.GetUtcNow());
    });

    public void Failed(SourceKind source, bool unauthorized) => Mutate(() =>
    {
        var now = timeProvider.GetUtcNow();
        switch (source)
        {
            case SourceKind.Players: _playersStatus = _playersStatus.Failed(now, unauthorized); break;
            case SourceKind.World: _worldStatus = _worldStatus.Failed(now, unauthorized); break;
            case SourceKind.Settings: _serverStatus = _serverStatus.Failed(now, unauthorized); break;
        }
    });

    private void Mutate(Action mutation)
    {
        SnapshotEnvelopeV1 snapshot;
        lock (_gate)
        {
            mutation();
            snapshot = CreateSnapshot();
        }
        queue.Publish(snapshot);
    }

    private SnapshotEnvelopeV1 CreateSnapshot()
    {
        var parts = transformer.Compose(_players ?? [], _world, _stageRefreshPending);
        return new SnapshotEnvelopeV1(
            SnapshotSchemaVersions.V1,
            typeof(SnapshotState).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            timeProvider.GetUtcNow(), _epoch, _sequence++,
            new LiveSnapshotV1(
                new SnapshotSection<IReadOnlyList<PublicPlayer>>(_playersStatus.ToContract(_players is not null), _players is null ? null : parts.Players),
                new SnapshotSection<PublicWorldData>(_worldStatus.ToContract(_world is not null), _world is null ? null : parts.World),
                new SnapshotSection<PublicServerDetails>(_serverStatus.ToContract(_server is not null), _server)));
    }

    private readonly record struct SectionStatus(
        SnapshotSourceState State,
        DateTimeOffset? LastAttempted,
        DateTimeOffset? LastSuccessful)
    {
        public SectionStatus Succeeded(DateTimeOffset now) => new(SnapshotSourceState.Healthy, now, now);
        public SectionStatus Failed(DateTimeOffset now, bool unauthorized) =>
            new(unauthorized ? SnapshotSourceState.Unauthorized : SnapshotSourceState.Unavailable, now, LastSuccessful);
        public SourceStatus ToContract(bool hasData) => new(
            State, (State is SnapshotSourceState.Unauthorized or SnapshotSourceState.Unavailable) && hasData,
            LastAttempted, LastSuccessful);
    }
}

internal sealed class LatestSnapshotQueue
{
    private readonly Channel<SnapshotEnvelopeV1> _channel = Channel.CreateBounded<SnapshotEnvelopeV1>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    public void Publish(SnapshotEnvelopeV1 snapshot) => _channel.Writer.TryWrite(snapshot);
    public ValueTask<SnapshotEnvelopeV1> Read(CancellationToken token) => _channel.Reader.ReadAsync(token);
}
