using System.Diagnostics.Metrics;

namespace Palmap.CollectorApi.Metrics;

internal static class CollectorMetrics
{
    public const string MeterName = "Palmap.Collector";

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ReporterSuccessTotal =
        Meter.CreateCounter<long>("palmap_reporter_success_total");

    public static readonly Counter<long> ReporterFailureTotal =
        Meter.CreateCounter<long>("palmap_reporter_failure_total");

    public static readonly Counter<long> IngestDeliveryTotal =
        Meter.CreateCounter<long>("palmap_ingest_delivery_total");

    public static readonly Histogram<double> IngestDeliveryDurationSeconds =
        Meter.CreateHistogram<double>("palmap_ingest_delivery_duration_seconds", unit: "s");

    private static readonly object LastSuccessGate = new();
    private static readonly Dictionary<string, long> LastSuccessUnixSeconds = new(StringComparer.Ordinal);

    public static void RecordReporterSuccess(string source, TimeProvider timeProvider)
    {
        ReporterSuccessTotal.Add(1, new KeyValuePair<string, object?>("source", source));
        var unix = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        lock (LastSuccessGate)
        {
            LastSuccessUnixSeconds[source] = unix;
        }
    }

    public static void RecordReporterFailure(string source, string reason)
    {
        ReporterFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", source),
            new KeyValuePair<string, object?>("reason", reason));
    }

    public static void RecordIngestDelivery(string outcome, double durationSeconds)
    {
        IngestDeliveryTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        IngestDeliveryDurationSeconds.Record(durationSeconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static IEnumerable<Measurement<long>> ObserveReporterLastSuccessTimestamps()
    {
        KeyValuePair<string, long>[] snapshot;
        lock (LastSuccessGate)
        {
            snapshot = LastSuccessUnixSeconds.ToArray();
        }

        foreach (var (source, unix) in snapshot)
        {
            yield return new Measurement<long>(unix, new KeyValuePair<string, object?>("source", source));
        }
    }
}
