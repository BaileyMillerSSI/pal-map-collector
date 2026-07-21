using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Palmap.Collector;

internal abstract class PollingService(
    SnapshotState state,
    CollectorOptions options,
    ILogger logger) : BackgroundService
{
    protected SnapshotState State { get; } = state;
    protected abstract SourceKind Source { get; }
    protected abstract TimeSpan Interval { get; }
    protected CollectorOptions Options { get; } = options;
    protected abstract Task Poll(CancellationToken token);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Poll(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                State.Failed(Source, exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden });
                logger.LogWarning("{Source} polling failed ({ExceptionType}); retained last-good data.", Source, exception.GetType().Name);
                await Task.Delay(Options.FailureInterval, stoppingToken);
            }
        }
    }
}

internal sealed class PlayersPollingService(
    PalworldApiClient client, SnapshotState state, CollectorOptions options,
    ILogger<PlayersPollingService> logger) : PollingService(state, options, logger)
{
    protected override SourceKind Source => SourceKind.Players;
    protected override TimeSpan Interval => Options.PlayersInterval;
    protected override async Task Poll(CancellationToken token) => State.PlayersSucceeded(await client.Players(token));
}

internal sealed class WorldPollingService(
    PalworldApiClient client, SnapshotState state, CollectorOptions options,
    ILogger<WorldPollingService> logger) : PollingService(state, options, logger)
{
    protected override SourceKind Source => SourceKind.World;
    protected override TimeSpan Interval => Options.WorldInterval;
    protected override async Task Poll(CancellationToken token)
    {
        State.WorldSucceeded(await client.World(token));
    }
}

internal sealed class SettingsPollingService(
    PalworldApiClient client, SnapshotState state, CollectorOptions options,
    ILogger<SettingsPollingService> logger) : PollingService(state, options, logger)
{
    protected override SourceKind Source => SourceKind.Settings;
    protected override TimeSpan Interval => Options.SettingsInterval;
    protected override async Task Poll(CancellationToken token)
    {
        State.SettingsSucceeded(await client.Settings(token));
    }
}
