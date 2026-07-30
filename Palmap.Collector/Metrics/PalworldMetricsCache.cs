using Palmap.PalworldApi.Models;

namespace Palmap.Collector.Metrics;

internal sealed class PalworldMetricsCache
{
    private readonly object _gate = new();
    private ServerMetricsResponse? _current;
    private bool _hasSample;

    public void Update(ServerMetricsResponse metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        lock (_gate)
        {
            _current = metrics;
            _hasSample = true;
        }
    }

    public bool TryGet(out ServerMetricsResponse metrics)
    {
        lock (_gate)
        {
            if (!_hasSample || _current is null)
            {
                metrics = new ServerMetricsResponse();
                return false;
            }

            metrics = _current;
            return true;
        }
    }
}
