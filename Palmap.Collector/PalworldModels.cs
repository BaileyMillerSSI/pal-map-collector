using System.Text.Json;
using System.Text.Json.Serialization;

namespace Palmap.Collector;

internal sealed record PalworldPlayerList(
    [property: JsonPropertyName("players")] IReadOnlyList<PalworldPlayer> Players);

internal sealed record PalworldPlayer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("playerId")] string PlayerId,
    [property: JsonPropertyName("userId")] string? UserId,
    [property: JsonPropertyName("ping")] double Ping,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("building_count")] int? BuildingCount);

internal sealed record PalworldWorldSnapshot(
    [property: JsonPropertyName("Time")] string Time,
    [property: JsonPropertyName("FPS")] double Fps,
    [property: JsonPropertyName("AverageFPS")] double AverageFps,
    [property: JsonPropertyName("InGameDays")] double? InGameDays,
    [property: JsonPropertyName("InGameTime")] JsonElement? InGameTime,
    [property: JsonPropertyName("ActorData")] IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> ActorData);

internal sealed record PalworldSettings
{
    public string? Difficulty { get; init; }
    public double DayTimeSpeedRate { get; init; }
    public double NightTimeSpeedRate { get; init; }
    public double? ExpRate { get; init; }
    public double? PalCaptureRate { get; init; }
    public double? PalSpawnNumRate { get; init; }
    public double? PalDamageRateAttack { get; init; }
    public double? PalDamageRateDefense { get; init; }
    public double? PlayerDamageRateAttack { get; init; }
    public double? PlayerDamageRateDefense { get; init; }
    public double? PlayerStomachDecreaceRate { get; init; }
    public double? PlayerStaminaDecreaceRate { get; init; }
    public double? PalStomachDecreaceRate { get; init; }
    public double? PalStaminaDecreaceRate { get; init; }
    public double? BuildObjectDamageRate { get; init; }
    public double? BuildObjectDeteriorationDamageRate { get; init; }
    public double? CollectionDropRate { get; init; }
    public double? CollectionObjectHpRate { get; init; }
    public double? CollectionObjectRespawnSpeedRate { get; init; }
    public double? EnemyDropItemRate { get; init; }
    public string? DeathPenalty { get; init; }
    public int BaseCampWorkerMaxNum { get; init; }
    public int? BaseCampMaxNumInGuild { get; init; }
    public int? GuildPlayerMaxNum { get; init; }
    public double? PalEggDefaultHatchingTime { get; init; }
    public double? WorkSpeedRate { get; init; }
    public double? ItemWeightRate { get; init; }
    public int? MaxBuildingLimitNum { get; init; }
    public double? SupplyDropSpan { get; init; }
    [JsonPropertyName("autoSaveSpan")] public double? AutoSaveSpan { get; init; }
    public int ServerPlayerMaxNum { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public string ServerDescription { get; init; } = string.Empty;
    public int PublicPort { get; init; }
    [JsonPropertyName("PublicIP")] public string PublicIp { get; init; } = string.Empty;
    public IReadOnlyList<string>? CrossplayPlatforms { get; init; }
    public string? AllowConnectPlatform { get; init; }
    [JsonPropertyName("bIsPvP")] public bool IsPvp { get; init; }
    [JsonPropertyName("bHardcore")] public bool? Hardcore { get; init; }
    [JsonPropertyName("bEnableFastTravel")] public bool? EnableFastTravel { get; init; }
    [JsonPropertyName("bEnableInvaderEnemy")] public bool? EnableInvaderEnemy { get; init; }
    [JsonPropertyName("bAllowClientMod")] public bool? AllowClientMod { get; init; }
    [JsonPropertyName("bIsUseBackupSaveData")] public bool? UseBackupSaveData { get; init; }
    [JsonPropertyName("bEnableVoiceChat")] public bool? EnableVoiceChat { get; init; }
}
