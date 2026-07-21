using System.Text.Json;
using Palmap.Collector;
using Palmap.Protocol;

namespace Palmap.UnitTests;

public sealed class PrivacyTests
{
    [Fact]
    public void FullTransformOmitsRawIdentifiersAndSensitiveSettings()
    {
        using var transformer = new PrivacyTransformer(TestData.Options());
        var players = transformer.SanitizePlayers(TestData.Players());
        var world = transformer.SanitizeWorld(TestData.World());
        var server = transformer.SanitizeSettings(TestData.Settings());
        var parts = transformer.Compose(players, world, new HashSet<string>());
        var now = DateTimeOffset.UnixEpoch;
        var healthy = new SourceStatus(SnapshotSourceState.Healthy, false, now, now);
        var envelope = new SnapshotEnvelopeV1(1, "1.0.0", now, Guid.NewGuid(), 1,
            new LiveSnapshotV1(new(healthy, parts.Players), new(healthy, parts.World), new(healthy, server)));
        var json = SnapshotContractV1.Serialize(envelope);

        Assert.Equal(43, parts.Players[0].Id.Length);
        Assert.Equal(parts.Players[0].GuildId, parts.World!.Guilds[0].Id);
        Assert.DoesNotContain("raw-player-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-user-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-guild-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("198.51.100.4", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicIP", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectionPreservesCoordinatesAndSuppressesInstances()
    {
        Assert.Equal(MapLayerId.WorldTree, MapProjection.Classify(348_000, -500_000));
        Assert.Equal(new PlayerLocation(PlayerLocationKind.Instance, null, null, null, "Dungeon"),
            MapProjection.PlayerLocation(1, 2, "Dungeon", false));
        Assert.Equal(new PlayerLocation(PlayerLocationKind.Unknown, null, null, null, null),
            MapProjection.PlayerLocation(1, 2, null, true));
        Assert.Equal(new PlayerLocation(PlayerLocationKind.Overworld, MapLayerId.Palpagos, 1, 2, null),
            MapProjection.PlayerLocation(1, 2, "none", false));
    }
}

internal static class TestData
{
    public static CollectorOptions Options() => new()
    {
        PalworldApiUrl = new Uri("http://palworld.test:8212"),
        PalworldAdminUsername = "synthetic-admin",
        PalworldAdminPassword = "synthetic-password",
        PalmapIngestUrl = new Uri("https://ingest.example.test/api/ingest/v1/snapshots"),
        PalmapClientId = "synthetic-client",
        PalmapClientSecret = "synthetic-secret",
        PrivacyKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()
    };

    public static PalworldPlayerList Players() => new([
        new PalworldPlayer("Synthetic", "raw-player-id", "raw-user-id", 12.6, 12_500, -4_200, 10, 4)
    ]);

    public static PalworldWorldSnapshot World() => new("12:00", 60, 59, 2, Json("\"14:00\""), [
        Actor("""{"Type":"PalBox","GuildID":"raw-guild-id","GuildName":"Synthetic Guild","LocationX":12000,"LocationY":-4000,"LocationZ":0}"""),
        Actor("""{"Type":"Character","UnitType":"BaseCampPal","GuildID":"raw-guild-id","level":10,"HP":100,"MaxHP":120,"LocationX":12010,"LocationY":-3990}"""),
        Actor("""{"Type":"Character","UnitType":"Player","userid":"raw-user-id","GuildID":"raw-guild-id","GuildName":"Synthetic Guild","Stage":"None","ip":"198.51.100.4"}""")
    ]);

    public static PalworldSettings Settings() => new()
    {
        ServerName = "Synthetic",
        ServerDescription = "Test",
        ServerPlayerMaxNum = 32,
        BaseCampWorkerMaxNum = 15,
        DayTimeSpeedRate = 1,
        NightTimeSpeedRate = 1,
        PublicIp = "198.51.100.4",
        PublicPort = 8211,
        AllowConnectPlatform = "(Steam, Xbox)",
        ExpRate = 1,
        PalCaptureRate = 1,
        PalSpawnNumRate = 1
    };

    private static JsonElement Json(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static Dictionary<string, JsonElement> Actor(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()); }
}
