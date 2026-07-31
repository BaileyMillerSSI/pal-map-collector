using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Palmap.PalworldApi;
using Palmap.PalworldApi.Services;
using Xunit;

namespace Palmap.IntegrationTests;

public sealed class ComposeIntegrationTests
{
    [IntegrationFact]
    public async Task PalworldEndpointsAndCollectorHealthAreAvailable()
    {
        var palworldUrl = Environment.GetEnvironmentVariable("PALMAP_PALWORLD_URL")
            ?? "http://127.0.0.1:8212";
        var password = Environment.GetEnvironmentVariable("PALMAP_PALWORLD_ADMIN_PASSWORD")
            ?? "palmap-integration";
        using var provider = CreatePalworldProvider(palworldUrl, password);
        using var palworld = provider.GetRequiredService<IPalworldApiServiceFactory>().Create();

        Assert.True(await palworld.Ping());
        Assert.False(string.IsNullOrWhiteSpace((await palworld.ServerInfo()).Version));
        Assert.NotNull((await palworld.PlayerList()).Players);
        var settings = await palworld.ServerSettings();
        Assert.True(settings.RestApiEnabled);
        Assert.Equal(8212, settings.RestApiPort);
        Assert.NotNull((await palworld.WorldActorSnapshot()).ActorData);
        Assert.True((await palworld.ServerMetrics()).MaxPlayerCount > 0);

        using var collector = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("PALMAP_COLLECTOR_URL")
                ?? "http://127.0.0.1:8080")
        };
        Assert.True((await collector.GetAsync("health/live")).IsSuccessStatusCode);
        Assert.True((await collector.GetAsync("health/ready")).IsSuccessStatusCode);

        using var metrics = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("PALMAP_METRICS_URL")
                ?? "http://127.0.0.1:9090")
        };
        string? metricsBody = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var metricsResponse = await metrics.GetAsync("metrics");
            if (metricsResponse.IsSuccessStatusCode)
            {
                metricsBody = await metricsResponse.Content.ReadAsStringAsync();
                if (metricsBody.Contains("palworld_server_fps", StringComparison.Ordinal) &&
                    metricsBody.Contains("palmap_ingest_queue_depth", StringComparison.Ordinal) &&
                    metricsBody.Contains("palworld_players_snapshot", StringComparison.Ordinal) &&
                    metricsBody.Contains("palworld_server_configured_max_players", StringComparison.Ordinal))
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        Assert.NotNull(metricsBody);
        Assert.Contains("palworld_server_fps", metricsBody, StringComparison.Ordinal);
        Assert.Contains("palmap_ingest_queue_depth", metricsBody, StringComparison.Ordinal);
        Assert.Contains("palworld_players_snapshot", metricsBody, StringComparison.Ordinal);
        Assert.Contains("palworld_server_configured_max_players", metricsBody, StringComparison.Ordinal);
        Assert.Contains("palworld_bases_total", metricsBody, StringComparison.Ordinal);
    }

    [IntegrationFact]
    public async Task InvalidPalworldCredentialsAreRejected()
    {
        var palworldUrl = Environment.GetEnvironmentVariable("PALMAP_PALWORLD_URL")
            ?? "http://127.0.0.1:8212";
        using var provider = CreatePalworldProvider(palworldUrl, "definitely-wrong");
        using var palworld = provider.GetRequiredService<IPalworldApiServiceFactory>().Create();

        Assert.False(await palworld.Ping());
        await Assert.ThrowsAsync<HttpRequestException>(() => palworld.ServerInfo());
    }

    private static ServiceProvider CreatePalworldProvider(string url, string password)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PalworldApi:BaseUrl"] = url,
            ["PalworldApi:Admin:Username"] = "admin",
            ["PalworldApi:Admin:Password"] = password
        });
        builder.AddPalworldApi();
        return builder.Services.BuildServiceProvider();
    }
}
