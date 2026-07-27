using System.Threading.Channels;

namespace Palmap.CollectorApi.Services.Internal;

internal sealed class GameDataRefreshSignal
{
    private readonly Channel<long> _requests = Channel.CreateBounded<long>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public void Request(long revision) =>
        _requests.Writer.TryWrite(revision);

    public async ValueTask<long> WaitForRevisionAfter(
        long completedRevision,
        CancellationToken cancellationToken)
    {
        while (await _requests.Reader.WaitToReadAsync(cancellationToken))
        {
            var requestedRevision = completedRevision;
            while (_requests.Reader.TryRead(out var revision))
            {
                requestedRevision = Math.Max(requestedRevision, revision);
            }

            if (requestedRevision > completedRevision)
            {
                return requestedRevision;
            }
        }

        throw new ChannelClosedException();
    }
}
