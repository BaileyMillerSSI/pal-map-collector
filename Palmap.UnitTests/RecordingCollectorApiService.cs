using Palmap.CollectorApi.Services;
using Palmap.PalworldApi.Models;

namespace Palmap.UnitTests;

internal sealed class RecordingCollectorApiService : ICollectorApiService
{
    public PlayerListResponse? Players { get; private set; }

    public WorldActorSnapshotResponse? Snapshot { get; private set; }

    public ServerSettingsResponse? Settings { get; private set; }

    public List<(CollectorSourceSection Section, CollectorSourceFailure Failure)> Failures { get; } = [];

    public Task ReportPlayerLocations(PlayerListResponse players, CancellationToken cancellationToken = default)
    {
        Players = players;
        return Task.CompletedTask;
    }

    public Task ReportGameData(WorldActorSnapshotResponse snapshot, CancellationToken cancellationToken = default)
    {
        Snapshot = snapshot;
        return Task.CompletedTask;
    }

    public Task ReportServerSettings(ServerSettingsResponse settings, CancellationToken cancellationToken = default)
    {
        Settings = settings;
        return Task.CompletedTask;
    }

    public Task ReportFailure(
        CollectorSourceSection section,
        CollectorSourceFailure failure,
        CancellationToken cancellationToken = default)
    {
        Failures.Add((section, failure));
        return Task.CompletedTask;
    }
}
