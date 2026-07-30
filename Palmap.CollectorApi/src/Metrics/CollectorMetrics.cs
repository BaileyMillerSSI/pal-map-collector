using System.Diagnostics.Metrics;

namespace Palmap.CollectorApi.Metrics;

internal sealed class CollectorMetrics : ICollectorMetricService
{
    public const string MeterName = "Palmap.Collector";

    private readonly TimeProvider _timeProvider;
    private readonly object _lastSuccessGate = new();
    private readonly Dictionary<string, long> _lastSuccessUnixSeconds = new(StringComparer.Ordinal);
    private readonly Counter<long> _reporterSuccessTotal;
    private readonly Counter<long> _reporterFailureTotal;
    private readonly Counter<long> _ingestDeliveryTotal;
    private readonly Histogram<double> _ingestDeliveryDurationSeconds;

    public CollectorMetrics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        Meter = new Meter(MeterName);
        _reporterSuccessTotal = Meter.CreateCounter<long>("palmap_reporter_success_total");
        _reporterFailureTotal = Meter.CreateCounter<long>("palmap_reporter_failure_total");
        _ingestDeliveryTotal = Meter.CreateCounter<long>("palmap_ingest_delivery_total");
        _ingestDeliveryDurationSeconds = Meter.CreateHistogram<double>(
            "palmap_ingest_delivery_duration_seconds",
            unit: "s");
    }

    public Meter Meter { get; }

    public void RecordReporterSuccess(string source)
    {
        _reporterSuccessTotal.Add(1, new KeyValuePair<string, object?>("source", source));
        var unix = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        lock (_lastSuccessGate)
        {
            _lastSuccessUnixSeconds[source] = unix;
        }
    }

    public void RecordReporterFailure(string source, string reason)
    {
        _reporterFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("source", source),
            new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordIngestDelivery(string outcome, double durationSeconds)
    {
        _ingestDeliveryTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _ingestDeliveryDurationSeconds.Record(
            durationSeconds,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public IEnumerable<Measurement<long>> ObserveReporterLastSuccessTimestamps()
    {
        KeyValuePair<string, long>[] snapshot;
        lock (_lastSuccessGate)
        {
            snapshot = _lastSuccessUnixSeconds.ToArray();
        }

        foreach (var (source, unix) in snapshot)
        {
            yield return new Measurement<long>(unix, new KeyValuePair<string, object?>("source", source));
        }
    }
}
