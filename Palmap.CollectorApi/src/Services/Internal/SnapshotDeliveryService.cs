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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var snapshot = await queue.Read(stoppingToken);
            var stableBody = SnapshotContractV1.SerializeToUtf8Bytes(snapshot);
            var delivered = false;
            for (var attempt = 1; attempt <= settings.CurrentValue.MaximumDeliveryAttempts; attempt++)
            {
                var result = await Send(stableBody, stoppingToken);
                if (result.Outcome == DeliveryOutcome.Accepted)
                {
                    logger.LogInformation("Delivered snapshot sequence {Sequence}.", snapshot.Sequence);
                    delivered = true;
                    break;
                }

                if (result.Outcome == DeliveryOutcome.Terminal)
                {
                    throw new InvalidOperationException(
                        "Palmap ingest rejected collector authentication or protocol compatibility.");
                }

                if (result.Outcome == DeliveryOutcome.Rejected)
                {
                    logger.LogWarning(
                        "Palmap ingest rejected snapshot sequence {Sequence}; moving to the latest snapshot.",
                        snapshot.Sequence);
                    break;
                }

                if (attempt < settings.CurrentValue.MaximumDeliveryAttempts)
                {
                    await Task.Delay(RetryDelay(attempt, result.RetryAfter), stoppingToken);
                }
            }

            if (!delivered)
            {
                logger.LogWarning(
                    "Delivery attempts ended for snapshot sequence {Sequence}; moving to the latest snapshot.",
                    snapshot.Sequence);
            }
        }
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
