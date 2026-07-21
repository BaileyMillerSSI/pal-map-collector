using System.Text.Json.Serialization;

namespace Palmap.PalworldApi.Models;

internal sealed record ServerSettingsResponse
{
    public string Difficulty { get; init; } = string.Empty;

    public double DayTimeSpeedRate { get; init; }

    public double NightTimeSpeedRate { get; init; }

    public double ExpRate { get; init; }

    public double PalCaptureRate { get; init; }

    public double PalSpawnNumRate { get; init; }

    public double PalDamageRateAttack { get; init; }

    public double PalDamageRateDefense { get; init; }

    public double PlayerDamageRateAttack { get; init; }

    public double PlayerDamageRateDefense { get; init; }

    public double PlayerStomachDecreaceRate { get; init; }

    public double PlayerStaminaDecreaceRate { get; init; }

    public double PlayerAutoHPRegeneRate { get; init; }

    public double PlayerAutoHpRegeneRateInSleep { get; init; }

    public double PalStomachDecreaceRate { get; init; }

    public double PalStaminaDecreaceRate { get; init; }

    public double PalAutoHPRegeneRate { get; init; }

    public double PalAutoHpRegeneRateInSleep { get; init; }

    public double BuildObjectDamageRate { get; init; }

    public double BuildObjectDeteriorationDamageRate { get; init; }

    public double CollectionDropRate { get; init; }

    public double CollectionObjectHpRate { get; init; }

    public double CollectionObjectRespawnSpeedRate { get; init; }

    public double EnemyDropItemRate { get; init; }

    public string DeathPenalty { get; init; } = string.Empty;

    [JsonPropertyName("bEnablePlayerToPlayerDamage")]
    public bool EnablePlayerToPlayerDamage { get; init; }

    [JsonPropertyName("bEnableFriendlyFire")]
    public bool EnableFriendlyFire { get; init; }

    [JsonPropertyName("bEnableInvaderEnemy")]
    public bool EnableInvaderEnemy { get; init; }

    [JsonPropertyName("bActiveUNKO")]
    public bool ActiveUnko { get; init; }

    [JsonPropertyName("bEnableAimAssistPad")]
    public bool EnableAimAssistPad { get; init; }

    [JsonPropertyName("bEnableAimAssistKeyboard")]
    public bool EnableAimAssistKeyboard { get; init; }

    public int DropItemMaxNum { get; init; }

    [JsonPropertyName("DropItemMaxNum_UNKO")]
    public int DropItemMaxNumUnko { get; init; }

    public int BaseCampMaxNum { get; init; }

    public int BaseCampWorkerMaxNum { get; init; }

    public double DropItemAliveMaxHours { get; init; }

    [JsonPropertyName("bAutoResetGuildNoOnlinePlayers")]
    public bool AutoResetGuildNoOnlinePlayers { get; init; }

    public double AutoResetGuildTimeNoOnlinePlayers { get; init; }

    public int GuildPlayerMaxNum { get; init; }

    public double PalEggDefaultHatchingTime { get; init; }

    public double WorkSpeedRate { get; init; }

    [JsonPropertyName("bIsMultiplay")]
    public bool IsMultiplay { get; init; }

    [JsonPropertyName("bIsPvP")]
    public bool IsPvp { get; init; }

    [JsonPropertyName("bCanPickupOtherGuildDeathPenaltyDrop")]
    public bool CanPickupOtherGuildDeathPenaltyDrop { get; init; }

    [JsonPropertyName("bEnableNonLoginPenalty")]
    public bool EnableNonLoginPenalty { get; init; }

    [JsonPropertyName("bEnableFastTravel")]
    public bool EnableFastTravel { get; init; }

    [JsonPropertyName("bIsStartLocationSelectByMap")]
    public bool IsStartLocationSelectByMap { get; init; }

    [JsonPropertyName("bExistPlayerAfterLogout")]
    public bool ExistPlayerAfterLogout { get; init; }

    [JsonPropertyName("bEnableDefenseOtherGuildPlayer")]
    public bool EnableDefenseOtherGuildPlayer { get; init; }

    public int CoopPlayerMaxNum { get; init; }

    public int ServerPlayerMaxNum { get; init; }

    public string ServerName { get; init; } = string.Empty;

    public string ServerDescription { get; init; } = string.Empty;

    public int PublicPort { get; init; }

    [JsonPropertyName("PublicIP")]
    public string PublicIp { get; init; } = string.Empty;

    [JsonPropertyName("RCONEnabled")]
    public bool RconEnabled { get; init; }

    [JsonPropertyName("RCONPort")]
    public int RconPort { get; init; }

    public string Region { get; init; } = string.Empty;

    [JsonPropertyName("bUseAuth")]
    public bool UseAuth { get; init; }

    [JsonPropertyName("BanListURL")]
    public string BanListUrl { get; init; } = string.Empty;

    [JsonPropertyName("RESTAPIEnabled")]
    public bool RestApiEnabled { get; init; }

    [JsonPropertyName("RESTAPIPort")]
    public int RestApiPort { get; init; }

    [JsonPropertyName("bShowPlayerList")]
    public bool ShowPlayerList { get; init; }

    public string AllowConnectPlatform { get; init; } = string.Empty;

    [JsonPropertyName("bIsUseBackupSaveData")]
    public bool IsUseBackupSaveData { get; init; }

    public string LogFormatType { get; init; } = string.Empty;
}
