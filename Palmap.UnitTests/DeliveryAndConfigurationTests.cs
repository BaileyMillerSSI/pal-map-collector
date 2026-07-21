using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Palmap.Collector;
using Palmap.Protocol;

namespace Palmap.UnitTests;

public sealed class DeliveryAndConfigurationTests
{
    [Theory]
    [InlineData(HttpStatusCode.Accepted, 0)]
    [InlineData(HttpStatusCode.Unauthorized, 3)]
    [InlineData(HttpStatusCode.UpgradeRequired, 3)]
    [InlineData(HttpStatusCode.TooManyRequests, 1)]
    [InlineData(HttpStatusCode.InternalServerError, 1)]
    [InlineData(HttpStatusCode.BadRequest, 2)]
    public async Task IngestClassifiesResponsesWithoutReadingBodies(HttpStatusCode status, int expected)
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(status)))
        {
            BaseAddress = new Uri("https://ingest.example.test/api/ingest/v1/snapshots")
        };

        var result = await new IngestClient(client, TimeProvider.System).Send("{}"u8.ToArray(), CancellationToken.None);

        Assert.Equal(expected, (int)result.Disposition);
    }

    [Fact]
    public async Task LatestQueueDropsIntermediateSnapshots()
    {
        var queue = new LatestSnapshotQueue();
        var fixture = SnapshotContractV1.Deserialize(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "snapshot-v1.synthetic.json")));
        queue.Publish(fixture with { Sequence = 1 });
        queue.Publish(fixture with { Sequence = 2 });
        queue.Publish(fixture with { Sequence = 3 });

        var latest = await queue.Read(CancellationToken.None);

        Assert.Equal(3, latest.Sequence);
    }

    [Fact]
    public async Task TimeoutAndExcessiveRetryAfterRemainBoundedRetries()
    {
        using var timedOut = new HttpClient(new AsyncHandler((_, _) => throw new TaskCanceledException()))
        {
            BaseAddress = new Uri("https://ingest.example.test/api/ingest/v1/snapshots")
        };
        var timeoutResult = await new IngestClient(timedOut, TimeProvider.System).Send("{}"u8.ToArray(), CancellationToken.None);

        using var throttled = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return response;
        }))
        { BaseAddress = timedOut.BaseAddress };
        var throttleResult = await new IngestClient(throttled, TimeProvider.System).Send("{}"u8.ToArray(), CancellationToken.None);

        Assert.Equal((int)DeliveryDisposition.Retry, (int)timeoutResult.Disposition);
        Assert.Equal(TimeSpan.FromMinutes(1), throttleResult.RetryAfter);
    }

    [Fact]
    public void HttpIngestRequiresBothDevelopmentAndExplicitOptIn()
    {
        var values = RequiredConfiguration();
        values["PALMAP_INGEST_URL"] = "http://ingest.example.test/api/ingest/v1/snapshots";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Throws<InvalidOperationException>(() => CollectorOptions.Load(configuration, new Environment("Development")));
        values["PALMAP_ALLOW_INSECURE_INGEST"] = "true";
        configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.NotNull(CollectorOptions.Load(configuration, new Environment("Development")));
        Assert.Throws<InvalidOperationException>(() => CollectorOptions.Load(configuration, new Environment("Production")));
    }

    private static Dictionary<string, string?> RequiredConfiguration() => new()
    {
        ["PALWORLD_API_URL"] = "http://palworld.test:8212",
        ["PALWORLD_ADMIN_USERNAME"] = "synthetic-admin",
        ["PALWORLD_ADMIN_PASSWORD"] = "synthetic-password",
        ["PALMAP_INGEST_URL"] = "https://ingest.example.test/api/ingest/v1/snapshots",
        ["PALMAP_CLIENT_ID"] = "synthetic-client",
        ["PALMAP_CLIENT_SECRET"] = "synthetic-secret",
        ["PALMAP_PRIVACY_KEY"] = Convert.ToBase64String(new byte[32])
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Palmap.UnitTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
