namespace Palmap.CollectorApi.Services;

internal sealed record GuildRuntimeMetrics(
    string GuildId,
    string GuildName,
    int InjuredBasePals,
    double HpDeficit,
    long BasePalLevelMax,
    double BasePalEstimatedPowerMax,
    int InactiveBasePals,
    int ActiveBasePals,
    int CompanionPals,
    long CompanionLevelMax,
    double CompanionEstimatedPowerMax);

internal sealed record WorldRuntimeMetrics(
    double CompanionLevelAvg,
    long CompanionLevelMax,
    double CompanionEstimatedPowerMax,
    double PlayerHpCurrent,
    double PlayerHpMax,
    int InjuredPlayers,
    long WildPalLevelMax);
