using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Palmap.CollectorApi.Configuration;
using Palmap.Protocol;

namespace Palmap.CollectorApi.Services.Internal;

internal sealed class SnapshotDeliveryService(
    LatestSnapshotQueue queue,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<PalmapIngestSettings> settings,
    TimeProvider timeProvider,
    ILogger<SnapshotDeliveryService> logger) : BackgroundService
{
    public const string HttpClientName = "PalmapIngest";

    private bool _deliveryDegraded;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var snapshot = await queue.Read(stoppingToken);
            var stableBody = SnapshotContractV1.SerializeToUtf8Bytes(snapshot);
            for (var attempt = 1; attempt <= settings.CurrentValue.MaximumDeliveryAttempts; attempt++)
            {
                var result = await Send(stableBody, stoppingToken);
                if (result.Outcome == DeliveryOutcome.Accepted)
                {
                    LogAccepted(snapshot.Sequence);
                    break;
                }

                if (result.Outcome == DeliveryOutcome.Terminal)
                {
                    throw new InvalidOperationException(
                        "Pal-Map ingest rejected collector authentication or protocol compatibility; snapshot " +
                        "delivery cannot continue. Verify the issued credentials and supported protocol version.");
                }

                if (result.Outcome == DeliveryOutcome.Rejected)
                {
                    LogRejected(snapshot.Sequence);
                    break;
                }

                LogRetry(snapshot.Sequence, attempt);
                if (attempt < settings.CurrentValue.MaximumDeliveryAttempts)
                {
                    await Task.Delay(RetryDelay(attempt, result.RetryAfter), stoppingToken);
                }
            }
        }
    }

    internal void LogAccepted(long sequence)
    {
        if (!_deliveryDegraded)
        {
            logger.LogDebug("Delivered snapshot sequence {Sequence}.", sequence);
            return;
        }

        logger.LogInformation(
            "Pal-Map ingest recovered; hosted map updates resumed with the latest available snapshot " +
            "after a delivery failure.");
        _deliveryDegraded = false;
    }

    internal void LogRetry(long sequence, int attempt)
    {
        if (!_deliveryDegraded)
        {
            _deliveryDegraded = true;
            logger.LogWarning(
                "Pal-Map ingest became unavailable; hosted map updates are delayed. The collector will retry " +
                "and keep only the latest snapshot; check network and hosted service health if this persists.");
        }

        logger.LogDebug(
            "Snapshot sequence {Sequence} delivery attempt {Attempt} of {MaximumAttempts} did not succeed.",
            sequence,
            attempt,
            settings.CurrentValue.MaximumDeliveryAttempts);
    }

    internal void LogRejected(long sequence)
    {
        if (_deliveryDegraded)
        {
            logger.LogDebug(
                "Pal-Map ingest is still rejecting snapshots; skipped sequence {Sequence}.",
                sequence);
            return;
        }

        _deliveryDegraded = true;
        logger.LogWarning(
            "Pal-Map ingest rejected a snapshot; the hosted map may be stale. Verify collector and hosted API " +
            "versions and inspect hosted ingest logs; the collector will continue with the latest state.");
    }

    internal async Task<DeliveryResult> Send(byte[] stableBody, CancellationToken stoppingToken)
    {
        var current = settings.CurrentValue;
        using var request = new HttpRequestMessage(HttpMethod.Post, current.Endpoint)
        {
            Content = new ByteArrayContent(stableBody)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var credentialBytes = Encoding.UTF8.GetBytes($"{current.ClientId}:{current.ClientSecret}");
        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(credentialBytes));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(credentialBytes);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(current.RequestTimeoutMs);
        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return new(DeliveryOutcome.Retry);
        }
        catch (HttpRequestException)
        {
            return new(DeliveryOutcome.Retry);
        }

        using (response)
        {
            return Classify(
                response.StatusCode,
                response.Headers.RetryAfter,
                timeProvider.GetUtcNow(),
                TimeSpan.FromMilliseconds(current.MaximumRetryDelayMs));
        }
    }

    internal static DeliveryResult Classify(
        HttpStatusCode statusCode,
        RetryConditionHeaderValue? retryAfter,
        DateTimeOffset now,
        TimeSpan maximumDelay) => statusCode switch
        {
            HttpStatusCode.Accepted => new(DeliveryOutcome.Accepted),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.UpgradeRequired =>
                new(DeliveryOutcome.Terminal),
            HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType =>
                new(DeliveryOutcome.Terminal),
            HttpStatusCode.BadRequest or HttpStatusCode.Conflict => new(DeliveryOutcome.Rejected),
            HttpStatusCode.TooManyRequests => new(
                DeliveryOutcome.Retry,
                BoundedRetryAfter(retryAfter, now, maximumDelay)),
            HttpStatusCode.RequestTimeout => new(DeliveryOutcome.Retry),
            >= HttpStatusCode.InternalServerError => new(DeliveryOutcome.Retry),
            _ => new(DeliveryOutcome.Rejected)
        };

    private TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter)
    {
        var maximum = TimeSpan.FromMilliseconds(settings.CurrentValue.MaximumRetryDelayMs);
        if (retryAfter is not null)
        {
            return retryAfter.Value;
        }

        var exponential = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1)));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
        return exponential + jitter > maximum ? maximum : exponential + jitter;
    }

    private static TimeSpan? BoundedRetryAfter(
        RetryConditionHeaderValue? retryAfter,
        DateTimeOffset now,
        TimeSpan maximum)
    {
        var value = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : null);
        return value is null ? null : TimeSpan.FromTicks(Math.Clamp(value.Value.Ticks, 0, maximum.Ticks));
    }
}

internal enum DeliveryOutcome { Accepted, Retry, Rejected, Terminal }
internal sealed record DeliveryResult(DeliveryOutcome Outcome, TimeSpan? RetryAfter = null);
