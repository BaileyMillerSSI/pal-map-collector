using Palmap.Protocol;

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
        Assert.Equal(snapshot, SnapshotContractV1.Deserialize(serialized));
        Assert.Equal(serialized, SnapshotContractV1.SerializeToUtf8Bytes(SnapshotContractV1.Deserialize(serialized)));
    }

    [Theory]
    [InlineData("rawPlayerId", "raw-player-id")]
    [InlineData("accountName", "raw-account-id")]
    [InlineData("ip", "198.51.100.4")]
    public void UnknownAndPrivateFieldsAreRejected(string field, string value)
    {
        var json = File.ReadAllText(FixturePath())
            .Replace("\"sequence\": 0", $"\"sequence\": 0, \"{field}\": \"{value}\"", StringComparison.Ordinal);

        Assert.Throws<SnapshotContractValidationException>(() => SnapshotContractV1.Deserialize(json));
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

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "snapshot-v1.synthetic.json");
}
