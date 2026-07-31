using System.Diagnostics.Metrics;
using System.Globalization;
using Palmap.Collector.Health;
using Palmap.CollectorApi.Metrics;
using Palmap.CollectorApi.Services;
using Palmap.CollectorApi.Services.Internal;
using Palmap.Protocol;

namespace Palmap.Collector.Metrics;

internal sealed class CollectorObservableMetrics(
    PalworldMetricsCache metricsCache,
    ICollectorMetricsSnapshotSource snapshotSource,
    IPalworldApiHealthService healthService,
    LatestSnapshotQueue snapshotQueue,
    ICollectorMetricService collectorMetrics)
{
    private int _registered;

    public void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        var meter = collectorMetrics.Meter;
        meter.CreateObservableGauge("palworld_server_fps", ObserveServerFps);
        meter.CreateObservableGauge(
            "palworld_server_frametime_milliseconds",
            ObserveServerFrametime,
            unit: "ms");
        meter.CreateObservableGauge("palworld_players_online", ObservePlayersOnline);
        meter.CreateObservableGauge("palworld_players_max", ObservePlayersMax);
        meter.CreateObservableGauge(
            "palworld_server_uptime_seconds",
            ObserveUptime,
            unit: "s");
        meter.CreateObservableGauge("palworld_base_camps_total", ObserveBaseCamps);
        meter.CreateObservableGauge("palworld_world_days", ObserveWorldDaysFromMetrics);

        meter.CreateObservableGauge("palworld_world_fps", ObserveWorldFps);
        meter.CreateObservableGauge("palworld_world_average_fps", ObserveWorldAverageFps);
        meter.CreateObservableGauge("palworld_world_ingame_days", ObserveWorldInGameDays);
        meter.CreateObservableGauge("palworld_world_ingame_time_minutes", ObserveWorldInGameTimeMinutes);
        meter.CreateObservableGauge("palworld_actors", ObserveActors);
        meter.CreateObservableGauge("palworld_actors_total", ObserveActorsTotal);
        meter.CreateObservableGauge("palworld_guilds_total", ObserveGuildsTotal);
        meter.CreateObservableGauge("palworld_bases_total", ObserveBasesTotal);
        meter.CreateObservableGauge("palworld_players_snapshot", ObservePlayersSnapshot);
        meter.CreateObservableGauge("palworld_guild_online_players", ObserveGuildOnlinePlayers);
        meter.CreateObservableGauge("palworld_guild_bases", ObserveGuildBases);
        meter.CreateObservableGauge("palworld_guild_base_pals", ObserveGuildBasePals);
        meter.CreateObservableGauge("palworld_guild_unassigned_base_pals", ObserveGuildUnassignedBasePals);
        meter.CreateObservableGauge("palworld_guild_buildings", ObserveGuildBuildings);
        meter.CreateObservableGauge("palworld_guild_estimated_power", ObserveGuildEstimatedPower);
        meter.CreateObservableGauge("palworld_guild_hp", ObserveGuildHp);
        meter.CreateObservableGauge(
            "palworld_player_ping_milliseconds_avg",
            ObservePlayerPingAvg,
            unit: "ms");
        meter.CreateObservableGauge(
            "palworld_player_ping_milliseconds_max",
            ObservePlayerPingMax,
            unit: "ms");
        meter.CreateObservableGauge("palworld_player_level_avg", ObservePlayerLevelAvg);
        meter.CreateObservableGauge("palworld_player_level_max", ObservePlayerLevelMax);
        meter.CreateObservableGauge("palworld_player_buildings_total", ObservePlayerBuildingsTotal);
        meter.CreateObservableGauge("palworld_players_by_location", ObservePlayersByLocation);

        meter.CreateObservableGauge("palworld_server_configured_max_players", ObserveConfiguredMaxPlayers);
        meter.CreateObservableGauge("palworld_server_max_pals_per_base", ObserveMaxPalsPerBase);
        meter.CreateObservableGauge("palworld_server_day_speed_rate", ObserveDaySpeedRate);
        meter.CreateObservableGauge("palworld_server_night_speed_rate", ObserveNightSpeedRate);
        meter.CreateObservableGauge("palworld_server_pvp_enabled", ObservePvpEnabled);
        meter.CreateObservableGauge("palworld_server_rule_rate", ObserveServerRuleRates);
        meter.CreateObservableGauge("palworld_server_rule_enabled", ObserveServerRuleEnabled);
        meter.CreateObservableGauge("palworld_server_death_penalty_info", ObserveDeathPenaltyInfo);

        meter.CreateObservableGauge("palmap_snapshot_sequence", ObserveSnapshotSequence);
        meter.CreateObservableGauge("palmap_snapshot_source_state", ObserveSnapshotSourceState);
        meter.CreateObservableGauge("palmap_snapshot_stale", ObserveSnapshotStale);
        meter.CreateObservableGauge("palmap_stage_refresh_pending", ObserveStageRefreshPending);
        meter.CreateObservableGauge("palmap_palworld_api_up", ObservePalworldApiUp);
        meter.CreateObservableGauge(
            "palmap_reporter_last_success_timestamp_seconds",
            collectorMetrics.ObserveReporterLastSuccessTimestamps,
            unit: "s");
        meter.CreateObservableGauge("palmap_ingest_queue_depth", () => snapshotQueue.Depth);
    }

    private IEnumerable<Measurement<long>> ObserveServerFps()
    {
        if (metricsCache.TryGet(out var metrics))
        {
            yield return new(metrics.ServerFps);
        }
    }

    private IEnumerable<Measurement<double>> ObserveServerFrametime()
    {
        if (metricsCache.TryGet(out var metrics))
        {
            yield return new(metrics.ServerFrameTimeMilliseconds);
        }
    }

    private IEnumerable<Measurement<long>> ObservePlayersOnline()
    {
        if (metricsCache.TryGet(out var metrics))
        {
            yield return new(metrics.CurrentPlayerCount);
        }
    }

    private IEnumerable<Measurement<long>> ObservePlayersMax()
    {
        if (metricsCache.TryGet(out var metrics))
        {
            yield return new(metrics.MaxPlayerCount);
        }
    }

    private IEnumerable<Measurement<long>> ObserveUptime()
    {
        if (metricsCache.TryGet(out var metrics))
        {
            yield return new(metrics.UptimeSeconds);
        }
    }

    private IEnumerable<Measurement<long>> ObserveBaseCamps()
    {
        if (metricsCache.TryGet(out var metrics))
        {
            yield return new(metrics.BaseCampCount);
        }
    }

    private IEnumerable<Measurement<long>> ObserveWorldDaysFromMetrics()
    {
        if (metricsCache.TryGet(out var metrics))
        {
            yield return new(metrics.Days);
        }
    }

    private IEnumerable<Measurement<double>> ObserveWorldFps()
    {
        if (snapshotSource.GetMetricsSnapshot().World is { Stats: { } stats })
        {
            yield return new(stats.Fps);
        }
    }

    private IEnumerable<Measurement<double>> ObserveWorldAverageFps()
    {
        if (snapshotSource.GetMetricsSnapshot().World is { Stats: { } stats })
        {
            yield return new(stats.AverageFps);
        }
    }

    private IEnumerable<Measurement<long>> ObserveWorldInGameDays()
    {
        if (snapshotSource.GetMetricsSnapshot().World is { Stats.InGameDays: { } days })
        {
            yield return new(days);
        }
    }

    private IEnumerable<Measurement<long>> ObserveWorldInGameTimeMinutes()
    {
        if (snapshotSource.GetMetricsSnapshot().World is { Stats.InGameTime: { } time } &&
            TryParseInGameTimeMinutes(time, out var minutes))
        {
            yield return new(minutes);
        }
    }

    private IEnumerable<Measurement<long>> ObserveActors()
    {
        if (snapshotSource.GetMetricsSnapshot().World is not { Stats.ActorCounts: { } counts })
        {
            yield break;
        }

        yield return Tagged(counts.Players, "player");
        yield return Tagged(counts.CompanionPals, "companion_pal");
        yield return Tagged(counts.BasePals, "base_pal");
        yield return Tagged(counts.WildPals, "wild_pal");
        yield return Tagged(counts.Npcs, "npc");
        yield return Tagged(counts.PalBoxes, "pal_box");
        yield return Tagged(counts.Other, "other");
    }

    private IEnumerable<Measurement<long>> ObserveActorsTotal()
    {
        if (snapshotSource.GetMetricsSnapshot().World is not { Stats.ActorCounts: { } counts })
        {
            yield break;
        }

        yield return new(
            counts.Players + counts.CompanionPals + counts.BasePals + counts.WildPals +
            counts.Npcs + counts.PalBoxes + counts.Other);
    }

    private IEnumerable<Measurement<long>> ObserveGuildsTotal()
    {
        if (snapshotSource.GetMetricsSnapshot().World is { Guilds: { } guilds })
        {
            yield return new(guilds.Count);
        }
    }

    private IEnumerable<Measurement<long>> ObserveBasesTotal()
    {
        if (snapshotSource.GetMetricsSnapshot().World is { Guilds: { } guilds })
        {
            yield return new(guilds.Sum(guild => guild.BaseCount));
        }
    }

    private IEnumerable<Measurement<long>> ObservePlayersSnapshot()
    {
        if (snapshotSource.GetMetricsSnapshot().Players is { } players)
        {
            yield return new(players.Count);
        }
    }

    private IEnumerable<Measurement<long>> ObserveGuildOnlinePlayers() =>
        GuildLong(snapshotSource.GetMetricsSnapshot().World?.Guilds, guild => guild.OnlinePlayerCount);

    private IEnumerable<Measurement<long>> ObserveGuildBases() =>
        GuildLong(snapshotSource.GetMetricsSnapshot().World?.Guilds, guild => guild.BaseCount);

    private IEnumerable<Measurement<long>> ObserveGuildBasePals() =>
        GuildLong(snapshotSource.GetMetricsSnapshot().World?.Guilds, guild => guild.BasePalCount);

    private IEnumerable<Measurement<long>> ObserveGuildUnassignedBasePals() =>
        GuildLong(snapshotSource.GetMetricsSnapshot().World?.Guilds, guild => guild.UnassignedBasePalCount);

    private IEnumerable<Measurement<long>> ObserveGuildBuildings() =>
        GuildLong(snapshotSource.GetMetricsSnapshot().World?.Guilds, guild => guild.KnownBuildingCount);

    private IEnumerable<Measurement<double>> ObserveGuildEstimatedPower() =>
        GuildDouble(snapshotSource.GetMetricsSnapshot().World?.Guilds, guild => guild.EstimatedPower);

    private IEnumerable<Measurement<double>> ObserveGuildHp()
    {
        if (snapshotSource.GetMetricsSnapshot().World?.Guilds is not { } guilds)
        {
            yield break;
        }

        foreach (var guild in guilds)
        {
            yield return GuildHpTagged(guild, guild.CurrentHp, "current");
            yield return GuildHpTagged(guild, guild.MaxHp, "max");
        }
    }

    private IEnumerable<Measurement<double>> ObservePlayerPingAvg()
    {
        if (snapshotSource.GetMetricsSnapshot().Players is { Count: > 0 } players)
        {
            yield return new(players.Average(player => player.PingMs));
        }
    }

    private IEnumerable<Measurement<long>> ObservePlayerPingMax()
    {
        if (snapshotSource.GetMetricsSnapshot().Players is { Count: > 0 } players)
        {
            yield return new(players.Max(player => player.PingMs));
        }
    }

    private IEnumerable<Measurement<double>> ObservePlayerLevelAvg()
    {
        if (snapshotSource.GetMetricsSnapshot().Players is { Count: > 0 } players)
        {
            yield return new(players.Average(player => player.Level));
        }
    }

    private IEnumerable<Measurement<long>> ObservePlayerLevelMax()
    {
        if (snapshotSource.GetMetricsSnapshot().Players is { Count: > 0 } players)
        {
            yield return new(players.Max(player => player.Level));
        }
    }

    private IEnumerable<Measurement<long>> ObservePlayerBuildingsTotal()
    {
        if (snapshotSource.GetMetricsSnapshot().Players is not { } players)
        {
            yield break;
        }

        yield return new(players.Sum(player => player.BuildingCount ?? 0));
    }

    private IEnumerable<Measurement<long>> ObservePlayersByLocation()
    {
        if (snapshotSource.GetMetricsSnapshot().Players is not { } players)
        {
            yield break;
        }

        yield return LocationTagged(players.Count(player => player.Location.Kind == PlayerLocationKind.Overworld), "overworld");
        yield return LocationTagged(players.Count(player => player.Location.Kind == PlayerLocationKind.Instance), "instance");
        yield return LocationTagged(players.Count(player => player.Location.Kind == PlayerLocationKind.Unknown), "unknown");
    }

    private IEnumerable<Measurement<long>> ObserveConfiguredMaxPlayers()
    {
        if (snapshotSource.GetMetricsSnapshot().Server is { } server)
        {
            yield return new(server.MaxPlayers);
        }
    }

    private IEnumerable<Measurement<long>> ObserveMaxPalsPerBase()
    {
        if (snapshotSource.GetMetricsSnapshot().Server is { } server)
        {
            yield return new(server.MaxPalsPerBase);
        }
    }

    private IEnumerable<Measurement<double>> ObserveDaySpeedRate()
    {
        if (snapshotSource.GetMetricsSnapshot().Server is { } server)
        {
            yield return new(server.DayTimeSpeedRate);
        }
    }

    private IEnumerable<Measurement<double>> ObserveNightSpeedRate()
    {
        if (snapshotSource.GetMetricsSnapshot().Server is { } server)
        {
            yield return new(server.NightTimeSpeedRate);
        }
    }

    private IEnumerable<Measurement<long>> ObservePvpEnabled()
    {
        if (snapshotSource.GetMetricsSnapshot().Server is { } server)
        {
            yield return new(server.PvpEnabled ? 1 : 0);
        }
    }

    private IEnumerable<Measurement<double>> ObserveServerRuleRates()
    {
        if (snapshotSource.GetMetricsSnapshot().Server is { Rules: { } rules })
        {
            foreach (var measurement in ServerRuleRates(rules))
            {
                yield return measurement;
            }
        }
    }

    private IEnumerable<Measurement<long>> ObserveServerRuleEnabled()
    {
        if (snapshotSource.GetMetricsSnapshot().Server is { Rules: { } rules })
        {
            foreach (var measurement in ServerRuleEnabled(rules))
            {
                yield return measurement;
            }
        }
    }

    private IEnumerable<Measurement<long>> ObserveDeathPenaltyInfo()
    {
        if (snapshotSource.GetMetricsSnapshot().Server is { Rules.DeathPenalty: { } penalty })
        {
            yield return new(1, new KeyValuePair<string, object?>("penalty", penalty));
        }
    }

    private IEnumerable<Measurement<long>> ObserveSnapshotSequence()
    {
        yield return new(snapshotSource.GetMetricsSnapshot().Sequence);
    }

    private IEnumerable<Measurement<long>> ObserveSnapshotSourceState()
    {
        var snapshot = snapshotSource.GetMetricsSnapshot();
        yield return StateTagged(snapshot.PlayersStatus.State, "players");
        yield return StateTagged(snapshot.WorldStatus.State, "world");
        yield return StateTagged(snapshot.ServerStatus.State, "server");
    }

    private IEnumerable<Measurement<long>> ObserveSnapshotStale()
    {
        var snapshot = snapshotSource.GetMetricsSnapshot();
        yield return StaleTagged(snapshot.PlayersStatus.IsStale, "players");
        yield return StaleTagged(snapshot.WorldStatus.IsStale, "world");
        yield return StaleTagged(snapshot.ServerStatus.IsStale, "server");
    }

    private IEnumerable<Measurement<long>> ObserveStageRefreshPending()
    {
        yield return new(snapshotSource.GetMetricsSnapshot().StageRefreshPendingCount);
    }

    private IEnumerable<Measurement<long>> ObservePalworldApiUp()
    {
        if (healthService.TryGetLastKnownHealthy(out var isHealthy))
        {
            yield return new(isHealthy ? 1 : 0);
        }
    }

    internal static bool TryParseInGameTimeMinutes(string? inGameTime, out long minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(inGameTime))
        {
            return false;
        }

        var parts = inGameTime.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mins) ||
            hours is < 0 or > 23 ||
            mins is < 0 or > 59)
        {
            return false;
        }

        minutes = hours * 60L + mins;
        return true;
    }

    internal static IEnumerable<Measurement<double>> ServerRuleRates(PublicServerRules rules)
    {
        if (rules.ExperienceRate is { } experience)
        {
            yield return RuleRate(experience, "experience");
        }

        if (rules.PalCaptureRate is { } capture)
        {
            yield return RuleRate(capture, "pal_capture");
        }

        if (rules.PalSpawnRate is { } spawn)
        {
            yield return RuleRate(spawn, "pal_spawn");
        }

        if (rules.WorkSpeedRate is { } work)
        {
            yield return RuleRate(work, "work_speed");
        }

        if (rules.EggHatchingHours is { } egg)
        {
            yield return RuleRate(egg, "egg_hatching_hours");
        }

        if (rules.ItemWeightRate is { } weight)
        {
            yield return RuleRate(weight, "item_weight");
        }

        if (rules.PlayerDamageDealtRate is { } playerDealt)
        {
            yield return RuleRate(playerDealt, "player_damage_dealt");
        }

        if (rules.PlayerDamageTakenRate is { } playerTaken)
        {
            yield return RuleRate(playerTaken, "player_damage_taken");
        }

        if (rules.PalDamageDealtRate is { } palDealt)
        {
            yield return RuleRate(palDealt, "pal_damage_dealt");
        }

        if (rules.PalDamageTakenRate is { } palTaken)
        {
            yield return RuleRate(palTaken, "pal_damage_taken");
        }

        if (rules.PlayerHungerRate is { } playerHunger)
        {
            yield return RuleRate(playerHunger, "player_hunger");
        }

        if (rules.PlayerStaminaRate is { } playerStamina)
        {
            yield return RuleRate(playerStamina, "player_stamina");
        }

        if (rules.PalHungerRate is { } palHunger)
        {
            yield return RuleRate(palHunger, "pal_hunger");
        }

        if (rules.PalStaminaRate is { } palStamina)
        {
            yield return RuleRate(palStamina, "pal_stamina");
        }

        if (rules.CollectionDropRate is { } collection)
        {
            yield return RuleRate(collection, "collection_drop");
        }

        if (rules.ResourceHealthRate is { } resourceHealth)
        {
            yield return RuleRate(resourceHealth, "resource_health");
        }

        if (rules.ResourceRespawnRate is { } resourceRespawn)
        {
            yield return RuleRate(resourceRespawn, "resource_respawn");
        }

        if (rules.EnemyDropRate is { } enemyDrop)
        {
            yield return RuleRate(enemyDrop, "enemy_drop");
        }

        if (rules.BuildingDamageRate is { } buildingDamage)
        {
            yield return RuleRate(buildingDamage, "building_damage");
        }

        if (rules.BuildingDeteriorationRate is { } buildingDeterioration)
        {
            yield return RuleRate(buildingDeterioration, "building_deterioration");
        }

        if (rules.AutosaveSeconds is { } autosave)
        {
            yield return RuleRate(autosave, "autosave_seconds");
        }

        if (rules.SupplyDropSeconds is { } supply)
        {
            yield return RuleRate(supply, "supply_drop_seconds");
        }

        if (rules.MaxBasesPerGuild is { } maxBases)
        {
            yield return RuleRate(maxBases, "max_bases_per_guild");
        }

        if (rules.MaxPlayersPerGuild is { } maxPlayers)
        {
            yield return RuleRate(maxPlayers, "max_players_per_guild");
        }

        if (rules.MaxBuildings is { } maxBuildings)
        {
            yield return RuleRate(maxBuildings, "max_buildings");
        }
    }

    internal static IEnumerable<Measurement<long>> ServerRuleEnabled(PublicServerRules rules)
    {
        if (rules.HardcoreEnabled is { } hardcore)
        {
            yield return RuleEnabled(hardcore, "hardcore");
        }

        if (rules.FastTravelEnabled is { } fastTravel)
        {
            yield return RuleEnabled(fastTravel, "fast_travel");
        }

        if (rules.InvasionsEnabled is { } invasions)
        {
            yield return RuleEnabled(invasions, "invasions");
        }

        if (rules.ClientModsAllowed is { } clientMods)
        {
            yield return RuleEnabled(clientMods, "client_mods");
        }

        if (rules.BackupsEnabled is { } backups)
        {
            yield return RuleEnabled(backups, "backups");
        }

        if (rules.VoiceChatEnabled is { } voiceChat)
        {
            yield return RuleEnabled(voiceChat, "voice_chat");
        }
    }

    private static IEnumerable<Measurement<long>> GuildLong(
        IReadOnlyList<PublicGuildAggregate>? guilds,
        Func<PublicGuildAggregate, long> value)
    {
        if (guilds is null)
        {
            yield break;
        }

        foreach (var guild in guilds)
        {
            yield return GuildTagged(guild, value(guild));
        }
    }

    private static IEnumerable<Measurement<double>> GuildDouble(
        IReadOnlyList<PublicGuildAggregate>? guilds,
        Func<PublicGuildAggregate, double> value)
    {
        if (guilds is null)
        {
            yield break;
        }

        foreach (var guild in guilds)
        {
            yield return new(
                value(guild),
                new KeyValuePair<string, object?>("guild_id", guild.Id),
                new KeyValuePair<string, object?>("guild_name", guild.Name));
        }
    }

    private static Measurement<long> GuildTagged(PublicGuildAggregate guild, long value) =>
        new(
            value,
            new KeyValuePair<string, object?>("guild_id", guild.Id),
            new KeyValuePair<string, object?>("guild_name", guild.Name));

    private static Measurement<double> GuildHpTagged(PublicGuildAggregate guild, double value, string kind) =>
        new(
            value,
            new KeyValuePair<string, object?>("guild_id", guild.Id),
            new KeyValuePair<string, object?>("guild_name", guild.Name),
            new KeyValuePair<string, object?>("kind", kind));

    private static Measurement<double> RuleRate(double value, string rule) =>
        new(value, new KeyValuePair<string, object?>("rule", rule));

    private static Measurement<long> RuleEnabled(bool enabled, string rule) =>
        new(enabled ? 1 : 0, new KeyValuePair<string, object?>("rule", rule));

    private static Measurement<long> Tagged(int value, string unitType) =>
        new(value, new KeyValuePair<string, object?>("unit_type", unitType));

    private static Measurement<long> LocationTagged(int value, string kind) =>
        new(value, new KeyValuePair<string, object?>("kind", kind));

    private static Measurement<long> StateTagged(SnapshotSourceState state, string section) =>
        new(
            (long)state,
            new KeyValuePair<string, object?>("section", section),
            new KeyValuePair<string, object?>("state", state.ToString().ToLowerInvariant()));

    private static Measurement<long> StaleTagged(bool isStale, string section) =>
        new(isStale ? 1 : 0, new KeyValuePair<string, object?>("section", section));
}
