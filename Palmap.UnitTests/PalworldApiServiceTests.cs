using System.Net;
using System.Text;
using Palmap.PalworldApi.Services.Internal;
using Polly.Timeout;

namespace Palmap.UnitTests;

public sealed class PalworldApiServiceTests
{
    [Fact]
    public async Task GetMethodsUseExpectedPathsAndDeserializeModels()
    {
        var responses = new Dictionary<string, string>
        {
            ["info"] = """{"version":"v1","servername":"Test","description":"Local","worldguid":"guid"}""",
            ["players"] = """{"players":[{"name":"PalUser","accountName":"paluser","playerId":"p1","userId":"u1","ip":"127.0.0.1","ping":3.14,"location_x":12.5,"location_y":18.5,"level":7,"building_count":2}]}""",
            ["settings"] = """{"ServerName":"Test","RESTAPIEnabled":true,"RESTAPIPort":8212,"bIsPvP":true,"PublicIP":"10.0.0.1","RCONEnabled":true,"RCONPort":25575,"BanListURL":"https://example.test/banlist","DropItemMaxNum_UNKO":9,"bShowPlayerList":true}""",
            ["game-data"] = """{"Time":"2026-06-17 13:00:40","FPS":59.5,"AverageFPS":58.2,"ActorData":[{"Type":"Character","InstanceID":"actor-1","UnitType":"Player","NickName":"PalUser","userid":"user-1","HP":100,"MaxHP":120,"LocationX":1,"LocationY":2,"LocationZ":3},{"Type":"PalBox","GuildID":"g1","LocationX":4,"LocationY":5,"LocationZ":6}]}""",
            ["metrics"] = """{"serverfps":57,"currentplayernum":1,"serverframetime":16.7,"maxplayernum":32,"uptime":3600,"basecampnum":2,"days":4}"""
        };
        var requestedPaths = new List<string>();
        using var client = new HttpClient(new TestHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.Segments[^1];
            requestedPaths.Add(path);
            return Json(HttpStatusCode.OK, responses[path]);
        }))
        {
            BaseAddress = new Uri("http://localhost:8212/v1/api/")
        };
        var service = new PalworldApiService(client);

        var info = await service.ServerInfo();
        var players = await service.PlayerList();
        var settings = await service.ServerSettings();
        var snapshot = await service.WorldActorSnapshot();
        var metrics = await service.ServerMetrics();

        Assert.Equal(["info", "players", "settings", "game-data", "metrics"], requestedPaths);
        Assert.Equal("Test", info.ServerName);
        Assert.Equal("PalUser", Assert.Single(players.Players).Name);
        Assert.True(settings.IsPvp);
        Assert.Equal("10.0.0.1", settings.PublicIp);
        Assert.True(settings.RconEnabled);
        Assert.Equal(25575, settings.RconPort);
        Assert.Equal("https://example.test/banlist", settings.BanListUrl);
        Assert.Equal(9, settings.DropItemMaxNumUnko);
        Assert.True(settings.ShowPlayerList);
        Assert.Equal(["Character", "PalBox"], snapshot.ActorData.Select(actor => actor.Type));
        Assert.Equal("actor-1", snapshot.ActorData[0].InstanceId);
        Assert.Equal("user-1", snapshot.ActorData[0].UserId);
        Assert.Equal(100, snapshot.ActorData[0].HitPoints);
        Assert.Equal("g1", snapshot.ActorData[1].GuildId);
        Assert.Equal(2, metrics.BaseCampCount);
    }

    [Fact]
    public async Task GetMethodThrowsForUnsuccessfulOrEmptyResponses()
    {
        using var unauthorizedClient = ClientReturning(HttpStatusCode.Unauthorized, "{}");
        using var emptyClient = ClientReturning(HttpStatusCode.OK, string.Empty);
        using var timedOutClient = new HttpClient(new TestHttpMessageHandler(_ => throw new TimeoutRejectedException()))
        {
            BaseAddress = new Uri("http://localhost:8212/v1/api/")
        };

        await Assert.ThrowsAsync<HttpRequestException>(() => new PalworldApiService(unauthorizedClient).ServerInfo());
        await Assert.ThrowsAnyAsync<Exception>(() => new PalworldApiService(emptyClient).ServerInfo());
        await Assert.ThrowsAsync<HttpRequestException>(() => new PalworldApiService(timedOutClient).ServerInfo());
    }

    [Fact]
    public async Task PingReturnsFalseForHttpAndTransportFailuresAndHonorsCancellation()
    {
        using var healthyClient = ClientReturning(HttpStatusCode.OK, "{}");
        using var unauthorizedClient = ClientReturning(HttpStatusCode.Unauthorized, "{}");
        using var failedClient = new HttpClient(new TestHttpMessageHandler(_ => throw new HttpRequestException("offline")))
        {
            BaseAddress = new Uri("http://localhost:8212/v1/api/")
        };
        using var canceledClient = new HttpClient(new TestHttpMessageHandler(_ => throw new OperationCanceledException()))
        {
            BaseAddress = new Uri("http://localhost:8212/v1/api/")
        };
        using var timedOutClient = new HttpClient(new TestHttpMessageHandler(_ => throw new TimeoutRejectedException()))
        {
            BaseAddress = new Uri("http://localhost:8212/v1/api/")
        };

        Assert.True(await new PalworldApiService(healthyClient).Ping());
        Assert.False(await new PalworldApiService(unauthorizedClient).Ping());
        Assert.False(await new PalworldApiService(failedClient).Ping());
        Assert.False(await new PalworldApiService(timedOutClient).Ping());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PalworldApiService(canceledClient).Ping(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task EmptyPlayerAndActorCollectionsRemainUsable()
    {
        using var playersClient = ClientReturning(HttpStatusCode.OK, """{"players":[]}""");
        using var actorsClient = ClientReturning(
            HttpStatusCode.OK,
            """{"Time":"2026-06-17 13:00:40","FPS":0,"AverageFPS":0,"ActorData":[]}""");

        Assert.Empty((await new PalworldApiService(playersClient).PlayerList()).Players);
        Assert.Empty((await new PalworldApiService(actorsClient).WorldActorSnapshot()).ActorData);
    }

    private static HttpClient ClientReturning(HttpStatusCode statusCode, string content) => new(
        new TestHttpMessageHandler(_ => Json(statusCode, content)))
    {
        BaseAddress = new Uri("http://localhost:8212/v1/api/")
    };

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

}
