using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Palmap.Protocol;

namespace Palmap.Collector;

internal enum OpaqueIdKind { Player, UserCorrelation, Guild, Base }

internal sealed class PrivacyTransformer(CollectorOptions options) : IDisposable
{
    private static ReadOnlySpan<byte> Context => "palworld-map:opaque-id:v1"u8;
    private readonly byte[] _privacyKey = options.PrivacyKey.ToArray();
    private readonly object _keyGate = new();

    public IReadOnlyList<SanitizedPlayer> SanitizePlayers(PalworldPlayerList response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var players = response.Players.Select(player =>
        {
            Require(!string.IsNullOrWhiteSpace(player.PlayerId) && !string.IsNullOrWhiteSpace(player.Name));
            Require(player.Level >= 0 && player.BuildingCount is null or >= 0);
            Require(double.IsFinite(player.Ping) && double.IsFinite(player.LocationX) && double.IsFinite(player.LocationY));
            return new SanitizedPlayer(
                Opaque(OpaqueIdKind.Player, player.PlayerId),
                string.IsNullOrWhiteSpace(player.UserId) ? null : Opaque(OpaqueIdKind.UserCorrelation, player.UserId),
                Clean(player.Name, 128), player.Level,
                Math.Max(0, checked((int)Math.Round(player.Ping, MidpointRounding.AwayFromZero))),
                player.BuildingCount, player.LocationX, player.LocationY);
        }).ToArray();
        return Array.AsReadOnly(players);
    }

    public SanitizedWorldProjection SanitizeWorld(PalworldWorldSnapshot response)
    {
        Require(!string.IsNullOrWhiteSpace(response.Time));
        Require(double.IsFinite(response.Fps) && response.Fps >= 0 && double.IsFinite(response.AverageFps) && response.AverageFps >= 0);
        var playerActors = new Dictionary<string, SanitizedPlayerContext>(StringComparer.Ordinal);
        var guilds = new Dictionary<string, GuildAccumulator>(StringComparer.Ordinal);
        var guildPals = new Dictionary<string, List<IReadOnlyDictionary<string, JsonElement>>>(StringComparer.Ordinal);
        var counts = new ActorCountAccumulator();

        GuildAccumulator GuildFor(string rawId, string? name)
        {
            if (!guilds.TryGetValue(rawId, out var guild))
            {
                guild = new GuildAccumulator(Opaque(OpaqueIdKind.Guild, rawId), Clean(name ?? "Unknown guild", 128));
                guilds.Add(rawId, guild);
            }
            return guild;
        }

        foreach (var actor in response.ActorData)
        {
            var type = StringField(actor, "Type");
            var unitType = StringField(actor, "UnitType");
            if (type == "PalBox")
            {
                counts.PalBoxes++;
                var rawGuildId = StringField(actor, "GuildID");
                var x = NumberField(actor, "LocationX", true);
                var y = NumberField(actor, "LocationY", true);
                var z = NumberField(actor, "LocationZ", true) ?? 0;
                if (rawGuildId is not null && x is not null && y is not null)
                    GuildFor(rawGuildId, StringField(actor, "GuildName")).Bases.Add(new BaseAccumulator(x.Value, y.Value, z));
                continue;
            }
            if (type != "Character") { counts.Other++; continue; }
            counts.Add(unitType);
            if (unitType == "Player")
            {
                var rawUserId = StringField(actor, "userid");
                var rawGuildId = StringField(actor, "GuildID");
                if (rawUserId is not null)
                {
                    var guildId = rawGuildId is null ? null : GuildFor(rawGuildId, StringField(actor, "GuildName")).Id;
                    playerActors[Opaque(OpaqueIdKind.UserCorrelation, rawUserId)] = new(guildId, StringField(actor, "Stage"));
                }
            }
            else if (unitType == "BaseCampPal")
            {
                var rawGuildId = StringField(actor, "GuildID");
                if (rawGuildId is null) continue;
                var guild = GuildFor(rawGuildId, StringField(actor, "GuildName"));
                guild.Add(actor);
                if (!guildPals.TryGetValue(rawGuildId, out var pals)) guildPals[rawGuildId] = pals = [];
                pals.Add(actor);
            }
        }

        foreach (var (rawGuildId, pals) in guildPals)
        {
            var guild = guilds[rawGuildId];
            guild.Bases.Sort(static (left, right) => left.X != right.X ? left.X.CompareTo(right.X) : left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.Z.CompareTo(right.Z));
            foreach (var pal in pals) AssignToNearestBase(guild, pal);
        }

        var projectedGuilds = guilds.Select(pair => pair.Value.Project(pair.Key, this))
            .OrderBy(guild => guild.Name, StringComparer.Ordinal).ThenBy(guild => guild.Id, StringComparer.Ordinal).ToArray();
        var inGameTime = response.InGameTime is { } time && time.ValueKind is not JsonValueKind.Null ? time.ToString() : null;
        var inGameDays = response.InGameDays is >= 0 and <= int.MaxValue ? (int?)response.InGameDays.Value : null;
        return new SanitizedWorldProjection(
            new PublicWorldStats(Clean(response.Time, 128), response.Fps, response.AverageFps, inGameDays, CleanOptional(inGameTime, 128), counts.ToPublic()),
            new ReadOnlyDictionary<string, SanitizedPlayerContext>(playerActors), Array.AsReadOnly(projectedGuilds));
    }

    public PublicServerDetails SanitizeSettings(PalworldSettings settings)
    {
        Require(settings.DayTimeSpeedRate >= 0 && double.IsFinite(settings.DayTimeSpeedRate));
        Require(settings.NightTimeSpeedRate >= 0 && double.IsFinite(settings.NightTimeSpeedRate));
        Require(settings.ServerPlayerMaxNum >= 0 && settings.BaseCampWorkerMaxNum >= 0);
        var platforms = settings.CrossplayPlatforms?.Select(value => Clean(value, 128)).Where(value => value.Length > 0).Take(32).ToArray();
        if (platforms is not { Length: > 0 })
            platforms = (settings.AllowConnectPlatform ?? string.Empty).Replace("(", string.Empty, StringComparison.Ordinal)
                .Replace(")", string.Empty, StringComparison.Ordinal).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => Clean(value, 128)).Take(32).ToArray();
        var rules = new PublicServerRules(
            CleanOptional(settings.Difficulty, 128), settings.ExpRate, settings.PalCaptureRate, settings.PalSpawnNumRate,
            settings.WorkSpeedRate, settings.PalEggDefaultHatchingTime, settings.ItemWeightRate, CleanOptional(settings.DeathPenalty, 128),
            settings.PlayerDamageRateAttack, settings.PlayerDamageRateDefense, settings.PalDamageRateAttack, settings.PalDamageRateDefense,
            settings.PlayerStomachDecreaceRate, settings.PlayerStaminaDecreaceRate, settings.PalStomachDecreaceRate, settings.PalStaminaDecreaceRate,
            settings.CollectionDropRate, settings.CollectionObjectHpRate, settings.CollectionObjectRespawnSpeedRate, settings.EnemyDropItemRate,
            settings.BuildObjectDamageRate, settings.BuildObjectDeteriorationDamageRate, settings.BaseCampMaxNumInGuild,
            settings.GuildPlayerMaxNum, settings.MaxBuildingLimitNum, settings.Hardcore, settings.EnableFastTravel,
            settings.EnableInvaderEnemy, settings.AllowClientMod, settings.UseBackupSaveData, settings.EnableVoiceChat,
            settings.AutoSaveSpan, settings.SupplyDropSpan);
        RequireRules(rules);
        return new PublicServerDetails(Clean(settings.ServerName, 128), Clean(settings.ServerDescription, 1024),
            Array.AsReadOnly(platforms), settings.ServerPlayerMaxNum, settings.BaseCampWorkerMaxNum,
            settings.DayTimeSpeedRate, settings.NightTimeSpeedRate, settings.IsPvp, rules, null);
    }

    public SanitizedSnapshotParts Compose(IReadOnlyList<SanitizedPlayer> players, SanitizedWorldProjection? world, IReadOnlySet<string> stageRefreshPending)
    {
        var counters = world?.Guilds.ToDictionary(guild => guild.Id, guild => new GuildOnlineAccumulator(guild), StringComparer.Ordinal);
        var publicPlayers = players.Select(player =>
        {
            SanitizedPlayerContext? context = null;
            if (player.UserCorrelationId is not null) world?.PlayersByUserId.TryGetValue(player.UserCorrelationId, out context);
            if (context?.GuildId is not null && counters?.TryGetValue(context.GuildId, out var guild) == true)
            {
                guild.OnlinePlayers++;
                if (player.BuildingCount is null) guild.BuildingCountComplete = false;
                else guild.KnownBuildingCount += player.BuildingCount.Value;
            }
            return new PublicPlayer(player.Id, player.Name, player.Level, player.PingMs, context?.GuildId, player.BuildingCount,
                MapProjection.PlayerLocation(player.X, player.Y, context?.Stage, stageRefreshPending.Contains(player.Id)));
        }).ToArray();
        var publicWorld = world is null || counters is null ? null : new PublicWorldData(world.Stats, Array.AsReadOnly(counters.Values.Select(value => value.ToPublic()).ToArray()));
        return new(Array.AsReadOnly(publicPlayers), publicWorld);
    }

    public void Dispose()
    {
        lock (_keyGate) CryptographicOperations.ZeroMemory(_privacyKey);
    }

    private string Opaque(OpaqueIdKind kind, string rawId)
    {
        var raw = Encoding.UTF8.GetBytes(rawId);
        var input = new byte[Context.Length + 5 + raw.Length];
        Context.CopyTo(input); input[Context.Length] = checked((byte)((int)kind + 1));
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(Context.Length + 1, 4), raw.Length);
        raw.CopyTo(input, Context.Length + 5);
        byte[] digest;
        lock (_keyGate) digest = HMACSHA256.HashData(_privacyKey, input);
        CryptographicOperations.ZeroMemory(raw); CryptographicOperations.ZeroMemory(input);
        try { return Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }

    private static void AssignToNearestBase(GuildAccumulator guild, IReadOnlyDictionary<string, JsonElement> pal)
    {
        var x = NumberField(pal, "LocationX", true); var y = NumberField(pal, "LocationY", true);
        if (x is null || y is null) { guild.UnassignedPalCount++; return; }
        var layer = MapProjection.Classify(x.Value, y.Value);
        var candidates = guild.Bases.Where(value => value.Layer == layer && layer is not null).ToArray();
        if (candidates.Length == 0) { guild.UnassignedPalCount++; return; }
        candidates.MinBy(value => Math.Pow(x.Value - value.X, 2) + Math.Pow(y.Value - value.Y, 2))!.Add(pal);
    }

    private static string? StringField(IReadOnlyDictionary<string, JsonElement> actor, string field) =>
        actor.TryGetValue(field, out var value) && value.ValueKind == JsonValueKind.String ? CleanOptional(value.GetString(), 128) : null;
    private static double? NumberField(IReadOnlyDictionary<string, JsonElement> actor, string field, bool allowNegative = false) =>
        actor.TryGetValue(field, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
        double.IsFinite(number) && (allowNegative || number >= 0) ? number : null;
    private static string Clean(string? value, int maximum) => new((value ?? string.Empty).Where(character => !char.IsControl(character)).Take(maximum).ToArray());
    private static string? CleanOptional(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : Clean(value.Trim(), maximum);
    private static void Require(bool value) { if (!value) throw new InvalidDataException("The upstream response did not match the expected shape."); }
    private static void RequireRules(PublicServerRules rules)
    {
        foreach (var property in typeof(PublicServerRules).GetProperties())
        {
            var value = property.GetValue(rules);
            Require(value is not double number || double.IsFinite(number) && number >= 0);
            Require(value is not int integer || integer >= 0);
        }
    }

    private class PalTotals
    {
        public int PalCount { get; private set; }
        public long TotalLevel { get; private set; }
        public double CurrentHp { get; private set; }
        public double MaxHp { get; private set; }
        public double EstimatedPower { get; private set; }
        public void Add(IReadOnlyDictionary<string, JsonElement> actor)
        {
            PalCount++; var level = NumberField(actor, "level"); var hp = NumberField(actor, "HP"); var maxHp = NumberField(actor, "MaxHP");
            if (level is not null) TotalLevel += checked((long)level.Value); if (hp is not null) CurrentHp += hp.Value;
            if (maxHp is not null) MaxHp += maxHp.Value; if (level is not null && hp is not null) EstimatedPower += level.Value * hp.Value;
        }
    }
    private sealed class BaseAccumulator(double x, double y, double z) : PalTotals
    {
        public double X { get; } = x; public double Y { get; } = y; public double Z { get; } = z;
        public MapLayerId? Layer { get; } = MapProjection.Classify(x, y);
    }
    private sealed class GuildAccumulator(string id, string name) : PalTotals
    {
        public string Id { get; } = id; public string Name { get; } = name; public int UnassignedPalCount { get; set; }
        public List<BaseAccumulator> Bases { get; } = [];
        public SanitizedGuild Project(string rawGuildId, PrivacyTransformer owner)
        {
            var bases = Bases.Select((value, index) => new PublicBaseAggregate(
                owner.Opaque(OpaqueIdKind.Base, BaseIdentity(rawGuildId, value.X, value.Y, value.Z)), $"Base {index + 1}",
                value.PalCount, value.TotalLevel, value.CurrentHp, value.MaxHp, value.EstimatedPower,
                value.Layer is { } layer ? new PublicMapPosition(layer, value.X, value.Y) : null)).ToArray();
            return new(Id, Name, PalCount, UnassignedPalCount, TotalLevel, CurrentHp, MaxHp, EstimatedPower, Array.AsReadOnly(bases));
        }
        private static string BaseIdentity(string guild, double x, double y, double z) => string.Create(CultureInfo.InvariantCulture, $"{guild.Length}:{guild}{(x == 0 ? 0 : x):R},{(y == 0 ? 0 : y):R},{(z == 0 ? 0 : z):R}");
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
        public void Add(string? unit) { switch (unit) { case "Player": Players++; break; case "OtomoPal": CompanionPals++; break; case "BaseCampPal": BasePals++; break; case "WildPal": WildPals++; break; case "NPC": Npcs++; break; default: Other++; break; } }
        public PublicActorCounts ToPublic() => new(Players, CompanionPals, BasePals, WildPals, Npcs, PalBoxes, Other);
    }
    private sealed class GuildOnlineAccumulator(SanitizedGuild guild)
    {
        public int OnlinePlayers { get; set; }
        public long KnownBuildingCount { get; set; }
        public bool BuildingCountComplete { get; set; } = true;
        public PublicGuildAggregate ToPublic() => new(guild.Id, guild.Name, OnlinePlayers, KnownBuildingCount, BuildingCountComplete,
            guild.Bases.Count, guild.PalCount, guild.UnassignedPalCount, guild.TotalLevel, guild.CurrentHp, guild.MaxHp, guild.EstimatedPower, guild.Bases);
    }
}

internal static class MapProjection
{
    private static readonly Bounds WorldTree = new(347_352.5, -818_196, 689_147.5, -476_401);
    private static readonly Bounds Palpagos = new(-1_099_399, -724_399, 349_399, 724_399);
    public static MapLayerId? Classify(double x, double y) => !double.IsFinite(x) || !double.IsFinite(y) ? null :
        WorldTree.Contains(x, y) ? MapLayerId.WorldTree : Palpagos.Contains(x, y) ? MapLayerId.Palpagos : null;
    public static bool IsInstancedStage(string? stage) => !string.IsNullOrWhiteSpace(stage) && !string.Equals(stage.Trim(), "none", StringComparison.OrdinalIgnoreCase);
    public static PlayerLocation PlayerLocation(double x, double y, string? stage, bool refreshPending)
    {
        if (refreshPending) return new(PlayerLocationKind.Unknown, null, null, null, null);
        if (IsInstancedStage(stage)) return new(PlayerLocationKind.Instance, null, null, null, stage!.Trim());
        var layer = Classify(x, y);
        return layer is null ? new(PlayerLocationKind.Unknown, null, null, null, null) : new(PlayerLocationKind.Overworld, layer, x, y, null);
    }
    private sealed record Bounds(double MinX, double MinY, double MaxX, double MaxY) { public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY; }
}

internal sealed record SanitizedPlayer(string Id, string? UserCorrelationId, string Name, int Level, int PingMs, int? BuildingCount, double X, double Y);
internal sealed record SanitizedPlayerContext(string? GuildId, string? Stage);
internal sealed record SanitizedGuild(string Id, string Name, int PalCount, int UnassignedPalCount, long TotalLevel, double CurrentHp, double MaxHp, double EstimatedPower, IReadOnlyList<PublicBaseAggregate> Bases);
internal sealed record SanitizedWorldProjection(PublicWorldStats Stats, IReadOnlyDictionary<string, SanitizedPlayerContext> PlayersByUserId, IReadOnlyList<SanitizedGuild> Guilds);
internal sealed record SanitizedSnapshotParts(IReadOnlyList<PublicPlayer> Players, PublicWorldData? World);
