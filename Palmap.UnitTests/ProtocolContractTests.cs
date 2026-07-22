using Palmap.Protocol;
using System.Text.Json.Nodes;

namespace Palmap.UnitTests;

public sealed class ProtocolContractTests
{
    [Fact]
    public void SyntheticFixtureStrictlyRoundTrips()
    {
        var fixture = File.ReadAllBytes(FixturePath());
        var snapshot = SnapshotContractV1.Deserialize(fixture);
        var serialized = SnapshotContractV1.SerializeToUtf8Bytes(snapshot);

        Assert.Equal(SnapshotSchemaVersions.V1, snapshot.SchemaVersion);
        Assert.Equal(3, Assert.IsAssignableFrom<IReadOnlyList<PublicPlayer>>(snapshot.Snapshot.Players.Data).Count);
        var world = Assert.IsType<PublicWorldData>(snapshot.Snapshot.World.Data);
        Assert.Single(world.Guilds);
        Assert.Single(world.Guilds[0].Bases);
        Assert.IsType<PublicServerDetails>(snapshot.Snapshot.Server.Data);
        var roundTripped = SnapshotContractV1.Deserialize(serialized);
        Assert.Equal(serialized, SnapshotContractV1.SerializeToUtf8Bytes(roundTripped));
    }

    [Theory]
    [InlineData("rawPlayerId", "raw-player-id")]
    [InlineData("accountName", "raw-account-id")]
    [InlineData("ip", "198.51.100.4")]
    public void UnknownAndPrivateFieldsAreRejected(string field, string value)
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        root[field] = value;

        Assert.Throws<SnapshotContractValidationException>(() =>
            SnapshotContractV1.Deserialize(root.ToJsonString()));
    }

    [Fact]
    public void RetainedFailureRequiresStaleDataAndSuccessfulTimestamp()
    {
        var pending = SnapshotContractV1.Deserialize(File.ReadAllBytes(FixturePath()));
        var invalid = pending with
        {
            Snapshot = pending.Snapshot with
            {
                Players = new SnapshotSection<IReadOnlyList<PublicPlayer>>(
                    new SourceStatus(SnapshotSourceState.Unavailable, false, pending.CollectedAt, null),
                    [new PublicPlayer("opaque-player-0001", "Synthetic", 1, 2, null, 0,
                        new PlayerLocation(PlayerLocationKind.Unknown, null, null, null, null))])
            }
        };

        Assert.Throws<SnapshotContractValidationException>(() => SnapshotContractV1.Validate(invalid));
    }

    [Fact]
    public void InstanceLocationCannotExposeCoordinates()
    {
        var pending = SnapshotContractV1.Deserialize(File.ReadAllBytes(FixturePath()));
        var now = pending.CollectedAt;
        var invalid = pending with
        {
            Snapshot = pending.Snapshot with
            {
                Players = new SnapshotSection<IReadOnlyList<PublicPlayer>>(
                    new SourceStatus(SnapshotSourceState.Healthy, false, now, now),
                    [new PublicPlayer("opaque-player-0001", "Synthetic", 1, 2, null, 0,
                        new PlayerLocation(PlayerLocationKind.Instance, null, 1, 2, "Dungeon"))])
            }
        };

        Assert.Throws<SnapshotContractValidationException>(() => SnapshotContractV1.Validate(invalid));
    }

    [Fact]
    public void RawInstanceIdentifierIsRejectedEvenWithoutCoordinates()
    {
        var pending = SnapshotContractV1.Deserialize(File.ReadAllBytes(FixturePath()));
        var now = pending.CollectedAt;
        var invalid = pending with
        {
            Snapshot = pending.Snapshot with
            {
                Players = new SnapshotSection<IReadOnlyList<PublicPlayer>>(
                    new SourceStatus(SnapshotSourceState.Healthy, false, now, now),
                    [new PublicPlayer("opaque-player-0001", "Synthetic", 1, 2, null, 0,
                        new PlayerLocation(PlayerLocationKind.Instance, null, null, null, "Dungeon_Boss_Secret"))])
            }
        };

        Assert.Throws<SnapshotContractValidationException>(() => SnapshotContractV1.Validate(invalid));
    }

    [Theory]
    [InlineData("Explorer 198.51.100.4")]
    [InlineData("Explorer [2001:db8::4]")]
    public void IpLiteralsAreRejectedInsideAllowedFreeText(string name)
    {
        var pending = SnapshotContractV1.Deserialize(File.ReadAllBytes(FixturePath()));
        var now = pending.CollectedAt;
        var invalid = pending with
        {
            Snapshot = pending.Snapshot with
            {
                Players = new SnapshotSection<IReadOnlyList<PublicPlayer>>(
                    new SourceStatus(SnapshotSourceState.Healthy, false, now, now),
                    [new PublicPlayer("opaque-player-0001", name, 1, 2, null, 0,
                        new PlayerLocation(PlayerLocationKind.Unknown, null, null, null, null))])
            }
        };

        Assert.Throws<SnapshotContractValidationException>(() => SnapshotContractV1.Validate(invalid));
    }

    [Fact]
    public void ExplicitNullNestedRequiredMembersProduceValidationException()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        var snapshot = root["snapshot"]!.AsObject();
        snapshot["world"] = JsonNode.Parse("""
            {
              "status": {
                "state": "healthy",
                "isStale": false,
                "lastAttemptedAt": "2026-07-21T12:00:00+00:00",
                "lastSuccessfulAt": "2026-07-21T12:00:00+00:00"
              },
              "data": {
                "stats": null,
                "guilds": [
                  null,
                  {
                    "id": "opaque-guild-0001",
                    "name": "Synthetic guild",
                    "onlinePlayerCount": 0,
                    "knownBuildingCount": 0,
                    "buildingCountComplete": true,
                    "baseCount": 0,
                    "basePalCount": 0,
                    "unassignedBasePalCount": 0,
                    "totalLevel": 0,
                    "currentHp": 0,
                    "maxHp": 0,
                    "estimatedPower": 0,
                    "bases": null
                  }
                ]
              }
            }
            """);
        snapshot["server"] = JsonNode.Parse("""
            {
              "status": {
                "state": "healthy",
                "isStale": false,
                "lastAttemptedAt": "2026-07-21T12:00:00+00:00",
                "lastSuccessfulAt": "2026-07-21T12:00:00+00:00"
              },
              "data": {
                "name": "Synthetic server",
                "description": "Synthetic description",
                "supportedPlatforms": [null],
                "maxPlayers": 0,
                "maxPalsPerBase": 0,
                "dayTimeSpeedRate": 1,
                "nightTimeSpeedRate": 1,
                "pvpEnabled": false,
                "rules": null
              }
            }
            """);

        Assert.Throws<SnapshotContractValidationException>(() =>
            SnapshotContractV1.Deserialize(root.ToJsonString()));
    }

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "snapshot-v1.synthetic.json");
}
