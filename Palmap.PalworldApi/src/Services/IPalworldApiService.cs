using Palmap.PalworldApi.Models;

namespace Palmap.PalworldApi.Services;

internal interface IPalworldApiService : IDisposable
{
    Task<ServerInfoResponse> ServerInfo(CancellationToken cancellationToken = default);
    Task<PlayerListResponse> PlayerList(CancellationToken cancellationToken = default);
    Task<ServerSettingsResponse> ServerSettings(CancellationToken cancellationToken = default);
    Task<WorldActorSnapshotResponse> WorldActorSnapshot(CancellationToken cancellationToken = default);
    Task<ServerMetricsResponse> ServerMetrics(CancellationToken cancellationToken = default);
    Task<bool> Ping(CancellationToken cancellationToken = default);
}
