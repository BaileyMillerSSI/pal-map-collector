using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Palmap.Protocol;

public static class SnapshotSchemaVersions
{
    public const int V1 = 1;
}

public static class SnapshotLimits
{
    public const long MaxSafeInteger = 9_007_199_254_740_991;
}

public enum SnapshotSourceState { Pending, Healthy, Unauthorized, Unavailable }

public enum MapLayerId
{
    Palpagos,
    [JsonStringEnumMemberName("world-tree")]
    WorldTree,
}

public enum PlayerLocationKind { Overworld, Instance, Unknown }

public sealed record SnapshotEnvelopeV1(
    [property: Range(1, 1)] int SchemaVersion,
    [property: Required, RegularExpression(@"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$")] string CollectorVersion,
    DateTimeOffset CollectedAt,
    Guid CollectorEpoch,
    [property: Range(0, SnapshotLimits.MaxSafeInteger)] long Sequence,
    [property: Required] LiveSnapshotV1 Snapshot);

public sealed record LiveSnapshotV1(
    [property: Required] SnapshotSection<IReadOnlyList<PublicPlayer>> Players,
    [property: Required] SnapshotSection<PublicWorldData> World,
    [property: Required] SnapshotSection<PublicServerDetails> Server);

public sealed record SnapshotSection<T>([property: Required] SourceStatus Status, T? Data);

public sealed record SourceStatus(
    SnapshotSourceState State,
    bool IsStale,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? LastSuccessfulAt);

public sealed record PublicPlayer(
    [property: Required, StringLength(128, MinimumLength = 16)] string Id,
    [property: Required, StringLength(128, MinimumLength = 1)] string Name,
    [property: Range(0, int.MaxValue)] int Level,
    [property: Range(0, int.MaxValue)] int PingMs,
    [property: StringLength(128, MinimumLength = 16)] string? GuildId,
    [property: Range(0, int.MaxValue)] int? BuildingCount,
    [property: Required] PlayerLocation Location);

public sealed record PlayerLocation(PlayerLocationKind Kind, MapLayerId? Layer, double? X, double? Y,
    [property: RegularExpression("^instance$")] string? Stage);

public sealed record PublicWorldData(
    [property: Required] PublicWorldStats Stats,
    [property: Required, MaxLength(1024)] IReadOnlyList<PublicGuildAggregate> Guilds);

public sealed record PublicActorCounts(
    [property: Range(0, int.MaxValue)] int Players,
    [property: Range(0, int.MaxValue)] int CompanionPals,
    [property: Range(0, int.MaxValue)] int BasePals,
    [property: Range(0, int.MaxValue)] int WildPals,
    [property: Range(0, int.MaxValue)] int Npcs,
    [property: Range(0, int.MaxValue)] int PalBoxes,
    [property: Range(0, int.MaxValue)] int Other);

public sealed record PublicWorldStats(
    [property: Required, StringLength(128)] string SourceTime,
    [property: Range(0, 1_000_000)] double Fps,
    [property: Range(0, 1_000_000)] double AverageFps,
    [property: Range(0, int.MaxValue)] int? InGameDays,
    [property: StringLength(128)] string? InGameTime,
    [property: Required] PublicActorCounts ActorCounts);

public sealed record PublicMapPosition(MapLayerId Layer, double X, double Y);

public sealed record PublicBaseAggregate(
    [property: Required, StringLength(128, MinimumLength = 16)] string Id,
    [property: Required, StringLength(128, MinimumLength = 1)] string Label,
    [property: Range(0, int.MaxValue)] int PalCount,
    [property: Range(0, SnapshotLimits.MaxSafeInteger)] long TotalLevel,
    [property: Range(0, 1_000_000_000_000_000d)] double CurrentHp,
    [property: Range(0, 1_000_000_000_000_000d)] double MaxHp,
    [property: Range(0, 1_000_000_000_000_000d)] double EstimatedPower,
    PublicMapPosition? Position);

public sealed record PublicGuildAggregate(
    [property: Required, StringLength(128, MinimumLength = 16)] string Id,
    [property: Required, StringLength(128, MinimumLength = 1)] string Name,
    [property: Range(0, int.MaxValue)] int OnlinePlayerCount,
    [property: Range(0, SnapshotLimits.MaxSafeInteger)] long KnownBuildingCount,
    bool BuildingCountComplete,
    [property: Range(0, int.MaxValue)] int BaseCount,
    [property: Range(0, int.MaxValue)] int BasePalCount,
    [property: Range(0, int.MaxValue)] int UnassignedBasePalCount,
    [property: Range(0, SnapshotLimits.MaxSafeInteger)] long TotalLevel,
    [property: Range(0, 1_000_000_000_000_000d)] double CurrentHp,
    [property: Range(0, 1_000_000_000_000_000d)] double MaxHp,
    [property: Range(0, 1_000_000_000_000_000d)] double EstimatedPower,
    [property: Required, MaxLength(128)] IReadOnlyList<PublicBaseAggregate> Bases);
