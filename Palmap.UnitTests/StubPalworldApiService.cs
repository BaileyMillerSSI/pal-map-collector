using Palmap.PalworldApi.Models;
using Palmap.PalworldApi.Services;

namespace Palmap.UnitTests;

internal sealed class StubPalworldApiService : IPalworldApiService, IPalworldApiServiceFactory
{
    private readonly Queue<bool> _pingResults = new();

    public PlayerListResponse Players { get; } = new()
    {
        Players = [new PalworldPlayer { Name = "PalUser" }]
    };

    public WorldActorSnapshotResponse Snapshot { get; } = new()
    {
        ActorData = [new WorldActor { Type = "Character" }]
    };

    public ServerSettingsResponse Settings { get; } = new()
    {
        ServerName = "Test"
    };

    public bool PingResult { get; set; } = true;

    public int PingCallCount { get; private set; }

    public int PlayerListCallCount { get; private set; }

    public Exception? PlayerListException { get; set; }

    public Task<ServerInfoResponse> ServerInfo(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServerInfoResponse());

    public Task<PlayerListResponse> PlayerList(CancellationToken cancellationToken = default)
    {
        PlayerListCallCount++;

        return PlayerListException is null
            ? Task.FromResult(Players)
            : Task.FromException<PlayerListResponse>(PlayerListException);
    }

    public Task<ServerSettingsResponse> ServerSettings(CancellationToken cancellationToken = default) =>
        Task.FromResult(Settings);

    public Task<WorldActorSnapshotResponse> WorldActorSnapshot(CancellationToken cancellationToken = default) =>
        Task.FromResult(Snapshot);

    public Task<ServerMetricsResponse> ServerMetrics(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServerMetricsResponse());

    public Task<bool> Ping(CancellationToken cancellationToken = default)
    {
        PingCallCount++;
        return Task.FromResult(_pingResults.TryDequeue(out var result) ? result : PingResult);
    }

    public void SetPingResults(params bool[] results)
    {
        foreach (var result in results)
        {
            _pingResults.Enqueue(result);
        }
    }

    public IPalworldApiService Create() => this;

    public void Dispose()
    {
    }
}
