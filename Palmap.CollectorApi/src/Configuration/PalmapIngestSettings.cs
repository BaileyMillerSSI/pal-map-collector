using System.ComponentModel.DataAnnotations;
using Palmap.Protocol;

namespace Palmap.CollectorApi.Configuration;

internal sealed record PalmapIngestSettings
{
    public const string SectionName = "PalmapIngest";
    public const string SnapshotV1Path = "/api/ingest/v1/snapshots";
    public const string DefaultEndpoint = PalmapIngress.DefaultBaseUrl + SnapshotV1Path;

    public bool Enabled { get; init; } = true;

    [Required]
    public string? Endpoint { get; init; } = DefaultEndpoint;

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? PrivacyKey { get; init; }

    public bool AllowInsecureHttp { get; init; }

    [Range(1, int.MaxValue)]
    public int RequestTimeoutMs { get; init; } = 20_000;

    [Range(1, 20)]
    public int MaximumDeliveryAttempts { get; init; } = 5;

    [Range(1, int.MaxValue)]
    public int MaximumRetryDelayMs { get; init; } = 60_000;
}
