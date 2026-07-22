using System.Threading.Channels;
using Palmap.Protocol;

namespace Palmap.CollectorApi.Services.Internal;

internal sealed class LatestSnapshotQueue
{
    private readonly Channel<SnapshotEnvelopeV1> _channel = Channel.CreateBounded<SnapshotEnvelopeV1>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public void Publish(SnapshotEnvelopeV1 snapshot) =>
        _channel.Writer.TryWrite(snapshot);

    public ValueTask<SnapshotEnvelopeV1> Read(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
