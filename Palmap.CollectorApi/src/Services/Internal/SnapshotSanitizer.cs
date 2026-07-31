using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Services;
using Palmap.PalworldApi.Models;
using Palmap.Protocol;

namespace Palmap.CollectorApi.Services.Internal;

internal sealed class SnapshotSanitizer(IOptionsMonitor<PalmapIngestSettings> settings)
{
    private static ReadOnlySpan<byte> OpaqueIdContext => "palworld-map:opaque-id:v1"u8;
    private byte[]? _ephemeralPrivacyKey;

    public IReadOnlyList<SanitizedPlayer> Players(PlayerListResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var privacyKey = PrivacyKey();
        try
        {
            var players = response.Players ?? throw new InvalidDataException("The player list was missing.");
            return Array.AsReadOnly(players.Select(player =>
            {
                if (player is null) throw new InvalidDataException("The player list contained a null entry.");
                Require(!string.IsNullOrWhiteSpace(player.PlayerId) && !string.IsNullOrWhiteSpace(player.Name));
                Require(player.Level >= 0 && player.BuildingCount is null or >= 0);
                Require(double.IsFinite(player.Ping) && player.Ping >= 0 &&
                    double.IsFinite(player.LocationX) && double.IsFinite(player.LocationY));
                return new SanitizedPlayer(
                    Opaque(privacyKey, OpaqueIdKind.Player, player.PlayerId),
                    string.IsNullOrWhiteSpace(player.UserId)
                        ? null
                        : Opaque(privacyKey, OpaqueIdKind.UserCorrelation, player.UserId),
                    CleanRequired(player.Name, "Unknown player", 128),
                    player.Level,
                    checked((int)Math.Min(int.MaxValue, Math.Round(player.Ping, MidpointRounding.AwayFromZero))),
                    player.BuildingCount,
                    player.LocationX,
                    player.LocationY);
            }).ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privacyKey);
        }
    }

    public SanitizedWorld World(WorldActorSnapshotResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Require(!string.IsNullOrWhiteSpace(response.Time));
        Require(double.IsFinite(response.Fps) && response.Fps >= 0 &&
            double.IsFinite(response.AverageFps) && response.AverageFps >= 0);
        var privacyKey = PrivacyKey();
        try
        {
            var playersByUser = new Dictionary<string, SanitizedPlayerContext>(StringComparer.Ordinal);
            var guilds = new Dictionary<string, GuildAccumulator>(StringComparer.Ordinal);
            var guildRuntime = new Dictionary<string, GuildRuntimeAccumulator>(StringComparer.Ordinal);
            var guildPals = new Dictionary<string, List<WorldActor>>(StringComparer.Ordinal);
            var companions = new List<WorldActor>();
            var counts = new ActorCountAccumulator();
            var worldRuntime = new WorldRuntimeAccumulator();

            GuildAccumulator GuildFor(string rawId, string? name)
            {
                var cleanedName = CleanRequired(name, "Unknown guild", 128);
                if (!guilds.TryGetValue(rawId, out var guild))
                {
                    guild = new GuildAccumulator(
                        Opaque(privacyKey, OpaqueIdKind.Guild, rawId),
                        cleanedName);
                    guilds.Add(rawId, guild);
                    guildRuntime.Add(rawId, new GuildRuntimeAccumulator(guild.Id, cleanedName));
                }
                else if (guild.Name == "Unknown guild" && cleanedName != "Unknown guild")
                {
                    guild.Name = cleanedName;
                    guildRuntime[rawId].GuildName = cleanedName;
                }

                return guild;
            }

            var actors = response.ActorData ?? throw new InvalidDataException("The actor list was missing.");
            foreach (var actor in actors)
            {
                if (actor is null) throw new InvalidDataException("The actor list contained a null entry.");
                if (actor.Type == "PalBox")
                {
                    counts.PalBoxes++;
                    if (!string.IsNullOrWhiteSpace(actor.GuildId) &&
                        actor.LocationX is { } x && actor.LocationY is { } y && actor.LocationZ is { } z &&
                        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z))
                    {
                        GuildFor(actor.GuildId, actor.GuildName).Bases.Add(new BaseAccumulator(x, y, z));
                    }

                    continue;
                }

                if (actor.Type != "Character")
                {
                    counts.Other++;
                    continue;
                }

                counts.Add(actor.UnitType);
                if (actor.UnitType == "Player")
                {
                    worldRuntime.AddPlayer(actor);
                    if (!string.IsNullOrWhiteSpace(actor.UserId))
                    {
                        var guildId = string.IsNullOrWhiteSpace(actor.GuildId)
                            ? null
                            : GuildFor(actor.GuildId, actor.GuildName).Id;
                        playersByUser[Opaque(privacyKey, OpaqueIdKind.UserCorrelation, actor.UserId)] =
                            new(guildId, MapProjection.IsInstancedStage(actor.Stage));
                    }
                }
                else if (actor.UnitType == "BaseCampPal" && !string.IsNullOrWhiteSpace(actor.GuildId))
                {
                    GuildFor(actor.GuildId, actor.GuildName).Add(actor);
                    guildRuntime[actor.GuildId].AddBasePal(actor);
                    if (!guildPals.TryGetValue(actor.GuildId, out var pals))
                    {
                        pals = [];
                        guildPals.Add(actor.GuildId, pals);
                    }

                    pals.Add(actor);
                }
                else if (actor.UnitType == "OtomoPal")
                {
                    worldRuntime.AddCompanion(actor);
                    companions.Add(actor);
                }
                else if (actor.UnitType == "WildPal")
                {
                    worldRuntime.AddWildPal(actor);
                }
            }

            foreach (var companion in companions)
            {
                if (!string.IsNullOrWhiteSpace(companion.GuildId) &&
                    guildRuntime.TryGetValue(companion.GuildId, out var runtime))
                {
                    runtime.AddCompanion(companion);
                }
            }

            foreach (var (rawGuildId, pals) in guildPals)
            {
                foreach (var pal in pals)
                {
                    guilds[rawGuildId].AssignToNearestBase(pal);
                }
            }

            var projectedGuilds = guilds.Select(pair => pair.Value.ToSanitized(pair.Key, privacyKey))
                .OrderBy(guild => guild.Name, StringComparer.Ordinal)
                .ThenBy(guild => guild.Id, StringComparer.Ordinal)
                .ToArray();
            var projectedGuildRuntime = guildRuntime.Values
                .Select(value => value.ToPublic())
                .OrderBy(guild => guild.GuildName, StringComparer.Ordinal)
                .ThenBy(guild => guild.GuildId, StringComparer.Ordinal)
                .ToArray();
            var inGameDays = response.InGameDays is { } days &&
                double.IsFinite(days) && days is >= 0 and <= int.MaxValue
                    ? (int?)days
                    : null;
            var inGameTime = response.InGameTime is { } time &&
                time.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    ? CleanOptional(time.ToString(), 128)
                    : null;
            return new SanitizedWorld(
                new PublicWorldStats(
                    CleanRequired(response.Time, "unknown", 128),
                    response.Fps,
                    response.AverageFps,
                    inGameDays,
                    inGameTime,
                    counts.ToPublic()),
                playersByUser,
                Array.AsReadOnly(projectedGuilds),
                Array.AsReadOnly(projectedGuildRuntime),
                worldRuntime.ToPublic());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privacyKey);
        }
    }

    public PublicServerDetails Server(ServerSettingsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Nonnegative(response.DayTimeSpeedRate);
        Nonnegative(response.NightTimeSpeedRate);
        Require(response.ServerPlayerMaxNum >= 0 && response.BaseCampWorkerMaxNum >= 0);
        var configuredPlatforms = response.CrossplayPlatforms is { Count: > 0 }
            ? response.CrossplayPlatforms
            : (response.AllowConnectPlatform ?? string.Empty)
                .Replace("(", string.Empty, StringComparison.Ordinal)
                .Replace(")", string.Empty, StringComparison.Ordinal)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var platforms = configuredPlatforms
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => CleanRequired(value, "Unknown", 128))
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToArray();
        var rules = new PublicServerRules(
            CleanOptional(response.Difficulty, 128),
            Checked(response.ExpRate),
            Checked(response.PalCaptureRate),
            Checked(response.PalSpawnNumRate),
            Checked(response.WorkSpeedRate),
            Checked(response.PalEggDefaultHatchingTime),
            Checked(response.ItemWeightRate),
            CleanOptional(response.DeathPenalty, 128),
            Checked(response.PlayerDamageRateAttack),
            Checked(response.PlayerDamageRateDefense),
            Checked(response.PalDamageRateAttack),
            Checked(response.PalDamageRateDefense),
            Checked(response.PlayerStomachDecreaceRate),
            Checked(response.PlayerStaminaDecreaceRate),
            Checked(response.PalStomachDecreaceRate),
            Checked(response.PalStaminaDecreaceRate),
            Checked(response.CollectionDropRate),
            Checked(response.CollectionObjectHpRate),
            Checked(response.CollectionObjectRespawnSpeedRate),
            Checked(response.EnemyDropItemRate),
            Checked(response.BuildObjectDamageRate),
            Checked(response.BuildObjectDeteriorationDamageRate),
            Checked(response.BaseCampMaxNumInGuild ?? response.BaseCampMaxNum),
            Checked(response.GuildPlayerMaxNum),
            Checked(response.MaxBuildingLimitNum),
            response.Hardcore,
            response.EnableFastTravel,
            response.EnableInvaderEnemy,
            response.AllowClientMod,
            response.IsUseBackupSaveData,
            response.EnableVoiceChat,
            Checked(response.AutoSaveSpan),
            Checked(response.SupplyDropSpan));
        return new PublicServerDetails(
            CleanRequired(response.ServerName, "Unnamed Palworld server", 128),
            CleanRequired(response.ServerDescription, "No description", 1024),
            Array.AsReadOnly(platforms),
            response.ServerPlayerMaxNum,
            response.BaseCampWorkerMaxNum,
            response.DayTimeSpeedRate,
            response.NightTimeSpeedRate,
            response.IsPvp,
            rules);
    }

    public SanitizedComposition Compose(
        IReadOnlyList<SanitizedPlayer>? players,
        SanitizedWorld? world,
        IReadOnlySet<string> stageRefreshPending)
    {
        var guilds = world?.Guilds.ToDictionary(
            guild => guild.Id,
            guild => new GuildOnlineAccumulator(guild),
            StringComparer.Ordinal);
        var publicPlayers = players?.Select(player =>
        {
            SanitizedPlayerContext? context = null;
            if (player.UserCorrelationId is not null)
            {
                world?.PlayersByUserId.TryGetValue(player.UserCorrelationId, out context);
            }

            if (context?.GuildId is not null && guilds?.TryGetValue(context.GuildId, out var guild) == true)
            {
                guild.OnlinePlayerCount++;
                if (player.BuildingCount is { } buildingCount)
                {
                    guild.KnownBuildingCount += buildingCount;
                }
                else
                {
                    guild.BuildingCountComplete = false;
                }
            }

            return new PublicPlayer(
                player.Id,
                player.Name,
                player.Level,
                player.PingMs,
                context?.GuildId,
                player.BuildingCount,
                MapProjection.PlayerLocation(
                    player.X,
                    player.Y,
                    context?.IsInstanced == true,
                    stageRefreshPending.Contains(player.Id)));
        }).ToArray();
        var publicWorld = world is null || guilds is null
            ? null
            : new PublicWorldData(
                world.Stats,
                Array.AsReadOnly(guilds.Values.Select(guild => guild.ToPublic()).ToArray()));
        return new(
            publicPlayers is null ? null : Array.AsReadOnly(publicPlayers),
            publicWorld);
    }

    private byte[] PrivacyKey()
    {
        var configured = settings.CurrentValue.PrivacyKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return PalmapIngestSettingsValidator.DecodePrivacyKey(configured);
        }

        if (!settings.CurrentValue.Enabled)
        {
            return _ephemeralPrivacyKey ??= RandomNumberGenerator.GetBytes(32);
        }

        return PalmapIngestSettingsValidator.DecodePrivacyKey(configured!);
    }

    private static string Opaque(byte[] privacyKey, OpaqueIdKind kind, string rawId)
    {
        var raw = Encoding.UTF8.GetBytes(rawId);
        var input = new byte[OpaqueIdContext.Length + 5 + raw.Length];
        OpaqueIdContext.CopyTo(input);
        input[OpaqueIdContext.Length] = checked((byte)((int)kind + 1));
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(OpaqueIdContext.Length + 1, 4), raw.Length);
        raw.CopyTo(input, OpaqueIdContext.Length + 5);
        try
        {
            var digest = HMACSHA256.HashData(privacyKey, input);
            try
            {
                return Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
            finally
            {
                CryptographicOperations.ZeroMemory(digest);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static double? Checked(double? value)
    {
        if (value is not null)
        {
            Nonnegative(value.Value);
        }

        return value;
    }

    private static int? Checked(int? value)
    {
        Require(value is null or >= 0);
        return value;
    }

    private static void Nonnegative(double value) =>
        Require(double.IsFinite(value) && value >= 0);

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidDataException("The upstream response did not match the expected public shape.");
        }
    }

    private static string CleanRequired(string? value, string fallback, int maximumLength)
    {
        var cleaned = new string((value ?? string.Empty).Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (cleaned.Length == 0 || ContainsIpAddress(cleaned))
        {
            cleaned = fallback;
        }

        return cleaned.Length <= maximumLength ? cleaned : cleaned[..maximumLength];
    }

    private static string? CleanOptional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) || ContainsIpAddress(value)
            ? null
            : CleanRequired(value, string.Empty, maximumLength);

    private static bool ContainsIpAddress(string value)
    {
        foreach (Match match in Regex.Matches(value, @"[0-9A-Fa-f:.]+", RegexOptions.CultureInvariant))
        {
            var candidate = match.Value.Trim('[', ']', '(', ')', ',', ';', '!', '?', '.');
            if ((candidate.Contains('.') || candidate.Contains(':')) && IPAddress.TryParse(candidate, out _))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ActorCountAccumulator
    {
        public int Players { get; private set; }
        public int CompanionPals { get; private set; }
        public int BasePals { get; private set; }
        public int WildPals { get; private set; }
        public int Npcs { get; private set; }
        public int PalBoxes { get; set; }
        public int Other { get; set; }

        public void Add(string? unitType)
        {
            switch (unitType)
            {
                case "Player": Players++; break;
                case "OtomoPal": CompanionPals++; break;
                case "BaseCampPal": BasePals++; break;
                case "WildPal": WildPals++; break;
                case "NPC": Npcs++; break;
                default: Other++; break;
            }
        }

        public PublicActorCounts ToPublic() =>
            new(Players, CompanionPals, BasePals, WildPals, Npcs, PalBoxes, Other);
    }

    private class PalTotals
    {
        public int PalCount { get; private set; }
        public long TotalLevel { get; private set; }
        public double CurrentHp { get; private set; }
        public double MaxHp { get; private set; }
        public double EstimatedPower { get; private set; }

        public void Add(WorldActor actor)
        {
            PalCount++;
            if (actor.Level is >= 0) TotalLevel += actor.Level.Value;
            if (actor.HitPoints is >= 0) CurrentHp += actor.HitPoints.Value;
            if (actor.MaxHitPoints is >= 0) MaxHp += actor.MaxHitPoints.Value;
            if (actor.Level is >= 0 && actor.HitPoints is >= 0)
                EstimatedPower += (double)actor.Level.Value * actor.HitPoints.Value;
        }
    }

    private sealed class BaseAccumulator(float x, float y, float z) : PalTotals
    {
        public float X { get; } = x;
        public float Y { get; } = y;
        public float Z { get; } = z;
        public MapLayerId? Layer { get; } = MapProjection.Classify(x, y);
    }

    private sealed class GuildAccumulator(string id, string name) : PalTotals
    {
        public string Id { get; } = id;
        public string Name { get; set; } = name;
        public List<BaseAccumulator> Bases { get; } = [];

        public void AssignToNearestBase(WorldActor actor)
        {
            if (actor.LocationX is not { } x || actor.LocationY is not { } y ||
                !float.IsFinite(x) || !float.IsFinite(y))
            {
                return;
            }

            var layer = MapProjection.Classify(x, y);
            var nearest = Bases
                .Where(@base => @base.Layer == layer && layer is not null)
                .MinBy(@base => Math.Pow(x - @base.X, 2) + Math.Pow(y - @base.Y, 2));
            nearest?.Add(actor);
        }

        public SanitizedGuild ToSanitized(string rawGuildId, byte[] privacyKey)
        {
            Bases.Sort(static (left, right) => left.X != right.X
                ? left.X.CompareTo(right.X)
                : left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.Z.CompareTo(right.Z));
            var bases = Bases.Select((value, index) => new PublicBaseAggregate(
                Opaque(privacyKey, OpaqueIdKind.Base, BaseIdentity(rawGuildId, value.X, value.Y, value.Z)),
                $"Base {index + 1}",
                value.PalCount,
                value.TotalLevel,
                value.CurrentHp,
                value.MaxHp,
                value.EstimatedPower,
                value.Layer is { } layer ? new PublicMapPosition(layer, value.X, value.Y) : null)).ToArray();
            return new(
                Id,
                Name,
                PalCount,
                TotalLevel,
                CurrentHp,
                MaxHp,
                EstimatedPower,
                Array.AsReadOnly(bases));
        }

        private static string BaseIdentity(string guildId, double x, double y, double z) =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{guildId.Length}:{guildId}{Canonical(x):R},{Canonical(y):R},{Canonical(z):R}");

        private static double Canonical(double value) => value == 0 ? 0 : value;
    }

    private sealed class GuildOnlineAccumulator(SanitizedGuild guild)
    {
        public int OnlinePlayerCount { get; set; }
        public long KnownBuildingCount { get; set; }
        public bool BuildingCountComplete { get; set; } = true;

        public PublicGuildAggregate ToPublic() => new(
            guild.Id,
            guild.Name,
            OnlinePlayerCount,
            KnownBuildingCount,
            BuildingCountComplete,
            guild.Bases.Count,
            guild.PalCount,
            guild.UnassignedBasePalCount,
            guild.TotalLevel,
            guild.CurrentHp,
            guild.MaxHp,
            guild.EstimatedPower,
            guild.Bases);
    }

    private sealed class GuildRuntimeAccumulator(string guildId, string guildName)
    {
        public string GuildId { get; } = guildId;
        public string GuildName { get; set; } = guildName;
        public int InjuredBasePals { get; private set; }
        public double HpDeficit { get; private set; }
        public long BasePalLevelMax { get; private set; }
        public double BasePalEstimatedPowerMax { get; private set; }
        public int InactiveBasePals { get; private set; }
        public int ActiveBasePals { get; private set; }
        public int CompanionPals { get; private set; }
        public long CompanionLevelMax { get; private set; }
        public double CompanionEstimatedPowerMax { get; private set; }

        public void AddBasePal(WorldActor actor)
        {
            AddInjury(actor, isBasePal: true);
            AddActivity(actor);
            if (actor.Level is >= 0)
            {
                BasePalLevelMax = Math.Max(BasePalLevelMax, actor.Level.Value);
            }

            if (TryEstimatedPower(actor, out var power))
            {
                BasePalEstimatedPowerMax = Math.Max(BasePalEstimatedPowerMax, power);
            }
        }

        public void AddCompanion(WorldActor actor)
        {
            CompanionPals++;
            if (actor.Level is >= 0)
            {
                CompanionLevelMax = Math.Max(CompanionLevelMax, actor.Level.Value);
            }

            if (TryEstimatedPower(actor, out var power))
            {
                CompanionEstimatedPowerMax = Math.Max(CompanionEstimatedPowerMax, power);
            }
        }

        private void AddInjury(WorldActor actor, bool isBasePal)
        {
            if (actor.HitPoints is not >= 0 || actor.MaxHitPoints is not >= 0)
            {
                return;
            }

            if (actor.HitPoints.Value < actor.MaxHitPoints.Value)
            {
                if (isBasePal)
                {
                    InjuredBasePals++;
                }

                HpDeficit += actor.MaxHitPoints.Value - actor.HitPoints.Value;
            }
        }

        private void AddActivity(WorldActor actor)
        {
            if (string.Equals(actor.IsActive, "false", StringComparison.OrdinalIgnoreCase))
            {
                InactiveBasePals++;
            }
            else if (string.Equals(actor.IsActive, "true", StringComparison.OrdinalIgnoreCase))
            {
                ActiveBasePals++;
            }
        }

        public GuildRuntimeMetrics ToPublic() => new(
            GuildId,
            GuildName,
            InjuredBasePals,
            HpDeficit,
            BasePalLevelMax,
            BasePalEstimatedPowerMax,
            InactiveBasePals,
            ActiveBasePals,
            CompanionPals,
            CompanionLevelMax,
            CompanionEstimatedPowerMax);
    }

    private sealed class WorldRuntimeAccumulator
    {
        private int _companionCount;
        private long _companionLevelSum;
        public long CompanionLevelMax { get; private set; }
        public double CompanionEstimatedPowerMax { get; private set; }
        public double PlayerHpCurrent { get; private set; }
        public double PlayerHpMax { get; private set; }
        public int InjuredPlayers { get; private set; }
        public long WildPalLevelMax { get; private set; }

        public void AddCompanion(WorldActor actor)
        {
            _companionCount++;
            if (actor.Level is >= 0)
            {
                _companionLevelSum += actor.Level.Value;
                CompanionLevelMax = Math.Max(CompanionLevelMax, actor.Level.Value);
            }

            if (TryEstimatedPower(actor, out var power))
            {
                CompanionEstimatedPowerMax = Math.Max(CompanionEstimatedPowerMax, power);
            }
        }

        public void AddPlayer(WorldActor actor)
        {
            if (actor.HitPoints is >= 0)
            {
                PlayerHpCurrent += actor.HitPoints.Value;
            }

            if (actor.MaxHitPoints is >= 0)
            {
                PlayerHpMax += actor.MaxHitPoints.Value;
            }

            if (actor.HitPoints is >= 0 &&
                actor.MaxHitPoints is >= 0 &&
                actor.HitPoints.Value < actor.MaxHitPoints.Value)
            {
                InjuredPlayers++;
            }
        }

        public void AddWildPal(WorldActor actor)
        {
            if (actor.Level is >= 0)
            {
                WildPalLevelMax = Math.Max(WildPalLevelMax, actor.Level.Value);
            }
        }

        public WorldRuntimeMetrics ToPublic() => new(
            _companionCount > 0 ? (double)_companionLevelSum / _companionCount : 0,
            CompanionLevelMax,
            CompanionEstimatedPowerMax,
            PlayerHpCurrent,
            PlayerHpMax,
            InjuredPlayers,
            WildPalLevelMax);
    }

    private static bool TryEstimatedPower(WorldActor actor, out double power)
    {
        if (actor.Level is >= 0 && actor.HitPoints is >= 0)
        {
            power = (double)actor.Level.Value * actor.HitPoints.Value;
            return true;
        }

        power = 0;
        return false;
    }
}

internal static class MapProjection
{
    private static readonly Bounds WorldTree = new(347_352.5, -818_196, 689_147.5, -476_401);
    private static readonly Bounds Palpagos = new(-1_099_399, -724_399, 349_399, 724_399);

    public static MapLayerId? Classify(double x, double y) =>
        !double.IsFinite(x) || !double.IsFinite(y)
            ? null
            : WorldTree.Contains(x, y)
                ? MapLayerId.WorldTree
                : Palpagos.Contains(x, y) ? MapLayerId.Palpagos : null;

    public static bool IsInstancedStage(string? stage) =>
        !string.IsNullOrWhiteSpace(stage) &&
        !string.Equals(stage.Trim(), "none", StringComparison.OrdinalIgnoreCase);

    public static PlayerLocation PlayerLocation(double x, double y, bool isInstanced, bool stageRefreshPending)
    {
        if (stageRefreshPending)
            return new(PlayerLocationKind.Unknown, null, null, null, null);
        if (isInstanced)
            return new(PlayerLocationKind.Instance, null, null, null, "instance");
        var layer = Classify(x, y);
        return layer is null
            ? new(PlayerLocationKind.Unknown, null, null, null, null)
            : new(PlayerLocationKind.Overworld, layer, x, y, null);
    }

    private sealed record Bounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public bool Contains(double x, double y) =>
            x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }
}

internal enum OpaqueIdKind { Player, UserCorrelation, Guild, Base }
internal sealed record SanitizedPlayer(
    string Id, string? UserCorrelationId, string Name, int Level, int PingMs,
    int? BuildingCount, double X, double Y);
internal sealed record SanitizedPlayerContext(string? GuildId, bool IsInstanced);
internal sealed record SanitizedGuild(
    string Id, string Name, int PalCount, long TotalLevel, double CurrentHp,
    double MaxHp, double EstimatedPower, IReadOnlyList<PublicBaseAggregate> Bases)
{
    public int UnassignedBasePalCount => Math.Max(0, PalCount - Bases.Sum(@base => @base.PalCount));
}
internal sealed record SanitizedWorld(
    PublicWorldStats Stats,
    IReadOnlyDictionary<string, SanitizedPlayerContext> PlayersByUserId,
    IReadOnlyList<SanitizedGuild> Guilds,
    IReadOnlyList<GuildRuntimeMetrics> GuildRuntime,
    WorldRuntimeMetrics WorldRuntime);
internal sealed record SanitizedComposition(
    IReadOnlyList<PublicPlayer>? Players,
    PublicWorldData? World);
