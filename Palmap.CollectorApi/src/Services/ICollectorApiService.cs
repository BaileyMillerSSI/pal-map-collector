using Palmap.PalworldApi.Models;

namespace Palmap.CollectorApi.Services;

internal interface ICollectorApiService
{
    Task ReportPlayerLocations(PlayerListResponse players, CancellationToken cancellationToken = default);
    Task ReportGameData(WorldActorSnapshotResponse snapshot, CancellationToken cancellationToken = default);
    Task ReportServerSettings(ServerSettingsResponse settings, CancellationToken cancellationToken = default);
}
