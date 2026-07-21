using System.ComponentModel.DataAnnotations;

namespace Palmap.CollectorApi.Configuration;

internal sealed record PalmapIngestSettings
{
    public const string SectionName = "PalmapIngest";

    [Required]
    public string? Endpoint { get; init; }

    [Required]
    public string? ClientId { get; init; }

    [Required]
    public string? ClientSecret { get; init; }

    [Required]
    public string? PrivacyKey { get; init; }

    public bool AllowInsecureHttp { get; init; }

    [Range(1, int.MaxValue)]
    public int RequestTimeoutMs { get; init; } = 20_000;

    [Range(1, 20)]
    public int MaximumDeliveryAttempts { get; init; } = 5;

    [Range(1, int.MaxValue)]
    public int MaximumRetryDelayMs { get; init; } = 60_000;
}
