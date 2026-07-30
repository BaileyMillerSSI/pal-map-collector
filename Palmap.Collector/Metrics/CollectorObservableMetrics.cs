using System.Diagnostics.Metrics;
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
        meter.CreateObservableGauge("palworld_actors", ObserveActors);
        meter.CreateObservableGauge("palworld_actors_total", ObserveActorsTotal);
        meter.CreateObservableGauge("palworld_guilds_total", ObserveGuildsTotal);
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
