using Palmap.Protocol;

namespace Palmap.CollectorApi.Services;

internal interface ICollectorMetricsSnapshotSource
{
    CollectorMetricsSnapshot GetMetricsSnapshot();
}

internal sealed record CollectorMetricsSnapshot(
    long Sequence,
    int StageRefreshPendingCount,
    SourceStatus PlayersStatus,
    SourceStatus WorldStatus,
    SourceStatus ServerStatus,
    IReadOnlyList<PublicPlayer>? Players,
    PublicWorldData? World,
    PublicServerDetails? Server,
    IReadOnlyList<GuildRuntimeMetrics>? GuildRuntime,
    WorldRuntimeMetrics? WorldRuntime);
