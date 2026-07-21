using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Palmap.Protocol;

public static class SnapshotContractV1
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    public static byte[] SerializeToUtf8Bytes(SnapshotEnvelopeV1 envelope)
    {
        Validate(envelope);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
    }

    public static string Serialize(SnapshotEnvelopeV1 envelope) => Encoding.UTF8.GetString(SerializeToUtf8Bytes(envelope));

    public static SnapshotEnvelopeV1 Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return Deserialize(Encoding.UTF8.GetBytes(json));
    }

    public static SnapshotEnvelopeV1 Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<SnapshotEnvelopeV1>(json, SerializerOptions)
                ?? throw new SnapshotContractValidationException(["Payload must be a JSON object."]);
            Validate(envelope);
            return envelope;
        }
        catch (JsonException exception)
        {
            throw new SnapshotContractValidationException(["Payload does not match snapshot schema version 1."], exception);
        }
    }

    public static void Validate(SnapshotEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var errors = new List<string>();
        ValidateObject(envelope, "$", errors, new HashSet<object>(ReferenceEqualityComparer.Instance));
        ValidateSemantics(envelope, errors);
        if (errors.Count > 0) throw new SnapshotContractValidationException(errors);
    }

    private static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private static void ValidateObject(object? value, string path, List<string> errors, HashSet<object> visited)
    {
        if (value is double number && !double.IsFinite(number)) { errors.Add($"{path}: Number must be finite."); return; }
        if (value is null || value is string || value.GetType().IsValueType || !visited.Add(value)) return;
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), results, true))
            errors.AddRange(results.Select(result => $"{path}: {result.ErrorMessage}"));
        if (value is IEnumerable enumerable)
        {
            var index = 0;
            foreach (var item in enumerable) ValidateObject(item, $"{path}[{index++}]", errors, visited);
            return;
        }
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            if (property.GetIndexParameters().Length == 0)
                ValidateObject(property.GetValue(value), $"{path}.{property.Name}", errors, visited);
    }

    private static void ValidateSemantics(SnapshotEnvelopeV1 envelope, List<string> errors)
    {
        if (envelope.SchemaVersion != SnapshotSchemaVersions.V1) errors.Add("$.schemaVersion: Only schema version 1 is supported.");
        if (envelope.CollectedAt == default || envelope.CollectedAt.Offset != TimeSpan.Zero) errors.Add("$.collectedAt: Timestamp must be UTC.");
        if (envelope.CollectorEpoch == Guid.Empty) errors.Add("$.collectorEpoch: Collector epoch must not be empty.");
        if (envelope.Snapshot is null) return;
        ValidateSection(envelope.Snapshot.Players, "$.snapshot.players", errors);
        ValidateSection(envelope.Snapshot.World, "$.snapshot.world", errors);
        ValidateSection(envelope.Snapshot.Server, "$.snapshot.server", errors);
        foreach (var player in envelope.Snapshot.Players?.Data ?? [])
        {
            if (player is null) errors.Add("$.snapshot.players.data: Entries must not be null.");
            else ValidateLocation(player.Location, errors);
        }
        ValidateRules(envelope.Snapshot.Server?.Data?.Rules, errors);
    }

    private static void ValidateSection<T>(SnapshotSection<T>? section, string path, List<string> errors)
    {
        if (section?.Status is null) return;
        var status = section.Status;
        if (status.LastAttemptedAt is { Offset: var attempted } && attempted != TimeSpan.Zero ||
            status.LastSuccessfulAt is { Offset: var successful } && successful != TimeSpan.Zero)
            errors.Add($"{path}.status: Timestamps must use UTC.");
        if (status.State == SnapshotSourceState.Pending && (status.IsStale || status.LastAttemptedAt is not null || status.LastSuccessfulAt is not null || section.Data is not null))
            errors.Add($"{path}: Pending sections cannot contain attempts, stale state, or data.");
        if (status.State == SnapshotSourceState.Healthy && (status.IsStale || status.LastAttemptedAt is null || status.LastSuccessfulAt is null || section.Data is null))
            errors.Add($"{path}: Healthy sections require current data and a successful timestamp.");
        if (status.IsStale && (status.LastSuccessfulAt is null || section.Data is null)) errors.Add($"{path}: Stale sections require retained data.");
        if (status.State is SnapshotSourceState.Unauthorized or SnapshotSourceState.Unavailable)
        {
            if (status.LastAttemptedAt is null) errors.Add($"{path}: Failed sections require an attempt timestamp.");
            if (section.Data is not null && !status.IsStale) errors.Add($"{path}: Retained failure data must be stale.");
        }
    }

    private static void ValidateLocation(PlayerLocation? location, List<string> errors)
    {
        var valid = location?.Kind switch
        {
            PlayerLocationKind.Overworld => location.Layer is not null && Finite(location.X) && Finite(location.Y) && location.Stage is null,
            PlayerLocationKind.Instance => location.Layer is null && location.X is null && location.Y is null && location.Stage == "instance",
            PlayerLocationKind.Unknown => location.Layer is null && location.X is null && location.Y is null && location.Stage is null,
            _ => false,
        };
        if (!valid) errors.Add("$.snapshot.players.data[].location: Fields do not match the selected location kind.");
    }

    private static bool Finite(double? value) => value is not null && double.IsFinite(value.Value);

    private static void ValidateRules(PublicServerRules? rules, List<string> errors)
    {
        if (rules is null) return;
        foreach (var property in typeof(PublicServerRules).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(rules);
            if (value is double number && (!double.IsFinite(number) || number < 0) || value is int integer && integer < 0)
                errors.Add($"$.snapshot.server.data.rules.{property.Name}: Value must be finite and nonnegative.");
        }
    }
}

public sealed class SnapshotContractValidationException(IReadOnlyList<string> errors, Exception? innerException = null)
    : Exception("Snapshot contract validation failed.", innerException)
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
