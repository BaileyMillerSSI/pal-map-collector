using System.Diagnostics.Metrics;

namespace Palmap.CollectorApi.Metrics;

internal interface ICollectorMetricService
{
    Meter Meter { get; }

    void RecordReporterSuccess(string source);

    void RecordReporterFailure(string source, string reason);

    void RecordIngestDelivery(string outcome, double durationSeconds);

    IEnumerable<Measurement<long>> ObserveReporterLastSuccessTimestamps();
}
