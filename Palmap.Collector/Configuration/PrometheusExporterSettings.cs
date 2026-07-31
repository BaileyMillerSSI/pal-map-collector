using System.ComponentModel.DataAnnotations;

namespace Palmap.Collector.Configuration;

internal sealed record PrometheusExporterSettings
{
    public const string SectionName = "PrometheusExporter";

    public bool Enabled { get; init; }

    [Required, MinLength(1)]
    public string Host { get; init; } = "+";

    [Range(1, 65535)]
    public int Port { get; init; } = 9090;

    [Range(1, int.MaxValue)]
    public int SampleIntervalMs { get; init; } = 15_000;
}
