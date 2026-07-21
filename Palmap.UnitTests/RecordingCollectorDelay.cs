using System.Threading.Channels;
using Palmap.Collector.Services;

namespace Palmap.UnitTests;

internal sealed class RecordingCollectorDelay : ICollectorDelay
{
    private readonly Channel<int> _delays = Channel.CreateUnbounded<int>();

    public async Task Delay(int milliseconds, CancellationToken cancellationToken)
    {
        await _delays.Writer.WriteAsync(milliseconds, cancellationToken);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public async Task<int> ReadNext(CancellationToken cancellationToken) =>
        await _delays.Reader.ReadAsync(cancellationToken);
}
