using System.ComponentModel.DataAnnotations;

namespace Palmap.CollectorApi.Configuration;

internal sealed record CollectorSettings
{
    public const string SectionName = "Collector";

    [Range(1, int.MaxValue)]
    public int PlayerLocationUpdateIntervalMs { get; init; } = 5_000;

    [Range(1, int.MaxValue)]
    public int GameDataUpdateIntervalMs { get; init; } = 30_000;

    [Range(1, int.MaxValue)]
    public int ServerSettingsUpdateIntervalMs { get; init; } = 3_600_000;

    [Range(1, int.MaxValue)]
    public int FailureRetryIntervalMs { get; init; } = 5_000;

    [Range(1, int.MaxValue)]
    public int PalworldHealthCacheDurationMs { get; init; } = 5_000;
}
