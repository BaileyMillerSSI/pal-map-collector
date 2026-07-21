using System.ComponentModel.DataAnnotations;

namespace Palmap.Protocol;

public sealed record PublicServerDetails(
    [property: Required, StringLength(128)] string Name,
    [property: Required, StringLength(1024)] string Description,
    [property: Required, MaxLength(32)] IReadOnlyList<string> SupportedPlatforms,
    [property: Range(0, int.MaxValue)] int MaxPlayers,
    [property: Range(0, int.MaxValue)] int MaxPalsPerBase,
    [property: Range(0, 1_000_000)] double DayTimeSpeedRate,
    [property: Range(0, 1_000_000)] double NightTimeSpeedRate,
    bool PvpEnabled,
    [property: Required] PublicServerRules Rules);

public sealed record PublicServerRules(
    [property: StringLength(128)] string? Difficulty,
    double? ExperienceRate, double? PalCaptureRate, double? PalSpawnRate, double? WorkSpeedRate,
    double? EggHatchingHours, double? ItemWeightRate,
    [property: StringLength(128)] string? DeathPenalty,
    double? PlayerDamageDealtRate, double? PlayerDamageTakenRate, double? PalDamageDealtRate,
    double? PalDamageTakenRate, double? PlayerHungerRate, double? PlayerStaminaRate,
    double? PalHungerRate, double? PalStaminaRate, double? CollectionDropRate,
    double? ResourceHealthRate, double? ResourceRespawnRate, double? EnemyDropRate,
    double? BuildingDamageRate, double? BuildingDeteriorationRate,
    int? MaxBasesPerGuild, int? MaxPlayersPerGuild, int? MaxBuildings,
    bool? HardcoreEnabled, bool? FastTravelEnabled, bool? InvasionsEnabled,
    bool? ClientModsAllowed, bool? BackupsEnabled, bool? VoiceChatEnabled,
    double? AutosaveSeconds, double? SupplyDropSeconds);
