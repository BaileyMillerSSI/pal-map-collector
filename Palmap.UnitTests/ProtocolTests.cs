using System.Text.Json;
using Palmap.Protocol;

namespace Palmap.UnitTests;

public sealed class ProtocolTests
{
    [Fact]
    public void SyntheticFixtureIsStrictValidAndRoundTrips()
    {
        var fixture = File.ReadAllBytes(FixturePath());
        var snapshot = SnapshotContractV1.Deserialize(fixture);
        var serialized = SnapshotContractV1.SerializeToUtf8Bytes(snapshot);
        var roundTrip = SnapshotContractV1.Deserialize(serialized);

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(snapshot, roundTrip);
        Assert.Equal(serialized, SnapshotContractV1.SerializeToUtf8Bytes(roundTrip));
    }

    [Theory]
    [InlineData("rawPlayerId")]
    [InlineData("ip")]
    [InlineData("accountName")]
    public void UnknownAndForbiddenFieldsFailDeserialization(string field)
    {
        var json = File.ReadAllText(FixturePath()).Replace("\"sequence\": 0", $"\"sequence\": 0, \"{field}\": \"forbidden\"", StringComparison.Ordinal);
        Assert.Throws<SnapshotContractValidationException>(() => SnapshotContractV1.Deserialize(json));
    }

    [Fact]
    public void InstanceLocationsCannotExposeCoordinates()
    {
        var pending = SnapshotContractV1.Deserialize(File.ReadAllBytes(FixturePath()));
        var now = pending.CollectedAt;
        var player = new PublicPlayer("opaque-player-0001", "Synthetic", 1, 2, null, null,
            new PlayerLocation(PlayerLocationKind.Instance, null, 1, 2, "Dungeon"));
        var invalid = pending with
        {
            Snapshot = pending.Snapshot with
            {
                Players = new SnapshotSection<IReadOnlyList<PublicPlayer>>(
                    new SourceStatus(SnapshotSourceState.Healthy, false, now, now), [player])
            }
        };

        Assert.Throws<SnapshotContractValidationException>(() => SnapshotContractV1.Validate(invalid));
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "fixtures", "snapshot-v1.synthetic.json");
}
