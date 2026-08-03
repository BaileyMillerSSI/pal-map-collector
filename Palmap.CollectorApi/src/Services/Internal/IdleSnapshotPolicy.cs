using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Palmap.CollectorApi.Configuration;
using Palmap.Protocol;

namespace Palmap.CollectorApi.Services.Internal;

internal sealed class IdleSnapshotPolicy(
    IOptionsMonitor<PalmapIngestSettings> settings,
    TimeProvider timeProvider)
{
    private PublicServerDetails? _acceptedServer;
    private byte[]? _acceptedDeliveryConfigurationDigest;
    private DateTimeOffset _acceptedAt;
    private int _acceptedSchemaVersion;
    private string? _acceptedCollectorVersion;
    private bool _armed;

    public bool ShouldDeliver(SnapshotEnvelopeV1 snapshot)
    {
        var current = settings.CurrentValue;
        if (!current.SuppressIdleSnapshots)
        {
            Reset();
            return true;
        }

        if (!IsCompleteHealthyEmpty(snapshot))
        {
            Reset();
            return true;
        }

        if (!_armed)
        {
            return true;
        }

        var heartbeatDue = _acceptedAt.AddMilliseconds(current.IdleSnapshotHeartbeatIntervalMs);
        if (timeProvider.GetUtcNow() >= heartbeatDue || !HasSamePublicConfiguration(snapshot))
        {
            Reset();
            return true;
        }

        return false;
    }

    public void MarkAccepted(SnapshotEnvelopeV1 snapshot)
    {
        if (!settings.CurrentValue.SuppressIdleSnapshots || !IsCompleteHealthyEmpty(snapshot))
        {
            Reset();
            return;
        }

        Reset();
        _acceptedSchemaVersion = snapshot.SchemaVersion;
        _acceptedCollectorVersion = snapshot.CollectorVersion;
        _acceptedServer = snapshot.Snapshot.Server.Data;
        _acceptedDeliveryConfigurationDigest = DeliveryConfigurationDigest(settings.CurrentValue);
        _acceptedAt = timeProvider.GetUtcNow();
        _armed = true;
    }

    internal static bool IsCompleteHealthyEmpty(SnapshotEnvelopeV1 snapshot) =>
        IsFreshAndHealthy(snapshot.Snapshot.Players.Status) &&
        snapshot.Snapshot.Players.Data is { Count: 0 } &&
        IsFreshAndHealthy(snapshot.Snapshot.World.Status) &&
        snapshot.Snapshot.World.Data is not null &&
        IsFreshAndHealthy(snapshot.Snapshot.Server.Status) &&
        snapshot.Snapshot.Server.Data is not null;

    private bool HasSamePublicConfiguration(SnapshotEnvelopeV1 snapshot)
    {
        var server = snapshot.Snapshot.Server.Data!;
        var deliveryConfigurationDigest = DeliveryConfigurationDigest(settings.CurrentValue);
        try
        {
            return snapshot.SchemaVersion == _acceptedSchemaVersion &&
                string.Equals(snapshot.CollectorVersion, _acceptedCollectorVersion, StringComparison.Ordinal) &&
                _acceptedDeliveryConfigurationDigest is not null &&
                CryptographicOperations.FixedTimeEquals(
                    deliveryConfigurationDigest,
                    _acceptedDeliveryConfigurationDigest) &&
                _acceptedServer is not null &&
                string.Equals(server.Name, _acceptedServer.Name, StringComparison.Ordinal) &&
                string.Equals(server.Description, _acceptedServer.Description, StringComparison.Ordinal) &&
                server.SupportedPlatforms.SequenceEqual(_acceptedServer.SupportedPlatforms, StringComparer.Ordinal) &&
                server.MaxPlayers == _acceptedServer.MaxPlayers &&
                server.MaxPalsPerBase == _acceptedServer.MaxPalsPerBase &&
                server.DayTimeSpeedRate == _acceptedServer.DayTimeSpeedRate &&
                server.NightTimeSpeedRate == _acceptedServer.NightTimeSpeedRate &&
                server.PvpEnabled == _acceptedServer.PvpEnabled &&
                server.Rules == _acceptedServer.Rules;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(deliveryConfigurationDigest);
        }
    }

    private static bool IsFreshAndHealthy(SourceStatus status) =>
        status.State == SnapshotSourceState.Healthy && !status.IsStale;

    private static byte[] DeliveryConfigurationDigest(PalmapIngestSettings current)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, current.Endpoint);
        Append(hash, current.ClientId);
        Append(hash, current.ClientSecret);
        Append(hash, current.PrivacyKey);
        Span<byte> fixedValues = stackalloc byte[9];
        fixedValues[0] = current.AllowInsecureHttp ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64BigEndian(fixedValues[1..], current.IdleSnapshotHeartbeatIntervalMs);
        hash.AppendData(fixedValues);
        return hash.GetHashAndReset();
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        try
        {
            hash.AppendData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private void Reset()
    {
        _acceptedServer = null;
        if (_acceptedDeliveryConfigurationDigest is not null)
        {
            CryptographicOperations.ZeroMemory(_acceptedDeliveryConfigurationDigest);
        }

        _acceptedDeliveryConfigurationDigest = null;
        _acceptedAt = default;
        _acceptedSchemaVersion = default;
        _acceptedCollectorVersion = null;
        _armed = false;
    }
}
