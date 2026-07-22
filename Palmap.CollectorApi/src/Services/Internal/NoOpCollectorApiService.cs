using Microsoft.Extensions.Logging;
using Palmap.PalworldApi.Models;

namespace Palmap.CollectorApi.Services.Internal;

internal sealed class NoOpCollectorApiService(ILogger<NoOpCollectorApiService> logger) : ICollectorApiService
{
    public Task ReportPlayerLocations(PlayerListResponse players, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Collected {PlayerCount} player locations; no collector backend is configured.", players.Players.Count);
        return Task.CompletedTask;
    }

    public Task ReportGameData(WorldActorSnapshotResponse snapshot, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Collected {ActorCount} world actors; no collector backend is configured.", snapshot.ActorData.Count);
        return Task.CompletedTask;
    }

    public Task ReportServerSettings(ServerSettingsResponse settings, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Collected server settings; no collector backend is configured.");
        return Task.CompletedTask;
    }

    public Task ReportFailure(
        CollectorSourceSection section,
        CollectorSourceFailure failure,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
