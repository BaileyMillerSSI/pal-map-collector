using Microsoft.Extensions.Logging;
using Palmap.Collector;
using Palmap.Collector.Health;
using Palmap.Collector.Logging;
using Palmap.Collector.Services;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Metrics;
using Palmap.CollectorApi.Services.Internal;
using Serilog.Events;

namespace Palmap.UnitTests;

public sealed class LoggingTests
{
    public static TheoryData<string> CollectorLifecycleMessages => new()
    {
        Program.StartedMessage,
        Program.StoppingMessage,
        Program.FatalMessage
    };

    [Theory]
    [MemberData(nameof(CollectorLifecycleMessages))]
    public void CollectorLifecycleMessagesUseTheCustomerFacingBrand(string message)
    {
        Assert.Contains("Pal-Map", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Palmap", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Trace", LogEventLevel.Verbose, false)]
    [InlineData("Debug", LogEventLevel.Debug, false)]
    [InlineData("Information", LogEventLevel.Information, false)]
    [InlineData("Warning", LogEventLevel.Warning, false)]
    [InlineData("Error", LogEventLevel.Error, false)]
    [InlineData("Critical", LogEventLevel.Fatal, false)]
    [InlineData("None", LogEventLevel.Fatal, true)]
    [InlineData(" debug ", LogEventLevel.Debug, false)]
    public void TopLevelLogLevelMapsStandardNames(
        string configured,
        LogEventLevel expectedMinimum,
        bool expectedDisabled)
    {
        var result = CollectorLogLevel.Parse(configured);

        Assert.Equal(expectedMinimum, result.MinimumLevel);
        Assert.Equal(expectedDisabled, result.Disabled);
    }

    [Fact]
    public void MissingLogLevelDefaultsToInformation()
    {
        var result = CollectorLogLevel.Parse(null);

        Assert.Equal("Information", result.Name);
        Assert.Equal(LogEventLevel.Information, result.MinimumLevel);
        Assert.False(result.Disabled);
    }

    [Fact]
    public void InvalidLogLevelIsActionableWithoutEchoingTheValue()
    {
        const string configured = "invalid-with-sensitive-text";

        var exception = Assert.Throws<InvalidOperationException>(() => CollectorLogLevel.Parse(configured));

        Assert.Contains("Trace, Debug, Information, Warning, Error, Critical, or None", exception.Message);
        Assert.DoesNotContain(configured, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthyPollDetailIsDebugAndDoesNotIncludePlayerData()
    {
        var logger = new RecordingLogger<PlayerLocationReporterTimedBackgroundService>();
        var palworld = new StubPalworldApiService();
        var worker = new PlayerLocationReporterTimedBackgroundService(
            palworld,
            new RecordingCollectorApiService(),
            new StaticOptionsMonitor<CollectorSettings>(new()),
            new StubPalworldApiHealthService(),
            new RecordingCollectorDelay(),
            new CollectorMetrics(TimeProvider.System),
            logger);

        await worker.ReportOnce(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.DoesNotContain("PalUser", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReporterFailuresWarnOnceThenDebugAndLogRecoveryWithoutExceptionText()
    {
        const string sensitiveText = "raw-player-id-and-server-address";
        var logger = new RecordingLogger<PlayerLocationReporterTimedBackgroundService>();
        var worker = new PlayerLocationReporterTimedBackgroundService(
            new StubPalworldApiService(),
            new RecordingCollectorApiService(),
            new StaticOptionsMonitor<CollectorSettings>(new()),
            new StubPalworldApiHealthService(),
            new RecordingCollectorDelay(),
            new CollectorMetrics(TimeProvider.System),
            logger);

        worker.LogSourceFailure(new InvalidDataException(sensitiveText));
        worker.LogSourceFailure(new InvalidDataException(sensitiveText));
        worker.LogSourceRecovery();

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Debug);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(sensitiveText, entry.Message, StringComparison.Ordinal);
            Assert.Null(entry.Exception);
        });
    }

    [Fact]
    public async Task PalworldHealthLogsOnlyDegradedAndRecoveredTransitionsAtInformationOrHigher()
    {
        var logger = new RecordingLogger<PalworldApiHealthService>();
        var palworld = new StubPalworldApiService { PingResult = true };
        var options = new StaticOptionsMonitor<CollectorSettings>(new CollectorSettings
        {
            PalworldHealthCacheDurationMs = 1
        });
        using var health = new PalworldApiHealthService(
            palworld,
            options,
            new CollectorDelay(),
            TimeProvider.System,
            logger);

        health.MarkUnhealthy();
        health.MarkUnhealthy();
        await Task.Delay(TimeSpan.FromMilliseconds(10));
        Assert.True(await health.IsHealthy());

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level >= LogLevel.Warning && entry.Message.Contains("available.", StringComparison.Ordinal));
    }

    [Fact]
    public void DeliveryRetriesWarnOnceThenRecoverAndKeepRoutineDetailAtDebug()
    {
        var logger = new RecordingLogger<SnapshotDeliveryService>();
        var service = DeliveryService(logger);

        service.LogRetry(10, 1);
        service.LogRetry(11, 2);
        service.LogAccepted(12);

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Debug));
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("example.test", StringComparison.Ordinal));
        AssertBrandedIngestMessages(logger.Entries);
    }

    [Fact]
    public void RejectedSnapshotsWarnOnceAndSummarizeRepetitionAtDebug()
    {
        var logger = new RecordingLogger<SnapshotDeliveryService>();
        var service = DeliveryService(logger);

        service.LogRejected(20);
        service.LogRejected(21);

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Debug);
        AssertBrandedIngestMessages(logger.Entries);
    }

    private static void AssertBrandedIngestMessages(IEnumerable<RecordedLogEntry> entries)
    {
        var branded = entries
            .Where(entry => entry.Message.Contains("ingest", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(branded);
        Assert.All(branded, entry =>
        {
            Assert.Contains("Pal-Map", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Palmap", entry.Message, StringComparison.Ordinal);
        });
    }

    private static SnapshotDeliveryService DeliveryService(ILogger<SnapshotDeliveryService> logger) => new(
        new LatestSnapshotQueue(),
        new StubHttpClientFactory(),
        new StaticOptionsMonitor<PalmapIngestSettings>(new PalmapIngestSettings
        {
            MaximumDeliveryAttempts = 5
        }),
        TimeProvider.System,
        new CollectorMetrics(TimeProvider.System),
        logger);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
