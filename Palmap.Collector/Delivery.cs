using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Palmap.Protocol;

namespace Palmap.Collector;

internal enum DeliveryDisposition { Accepted, Retry, Rejected, Terminal }
internal sealed record DeliveryResult(DeliveryDisposition Disposition, TimeSpan? RetryAfter = null);

internal sealed class IngestClient(HttpClient client, TimeProvider timeProvider)
{
    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromMinutes(1);

    public async Task<DeliveryResult> Send(byte[] json, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new ByteArrayContent(json)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new(DeliveryDisposition.Retry);
        }
        using (response)
        {
            return response.StatusCode switch
            {
                HttpStatusCode.Accepted => new(DeliveryDisposition.Accepted),
                HttpStatusCode.Unauthorized or HttpStatusCode.UpgradeRequired => new(DeliveryDisposition.Terminal),
                HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType => new(DeliveryDisposition.Terminal),
                HttpStatusCode.BadRequest or HttpStatusCode.Conflict => new(DeliveryDisposition.Rejected),
                HttpStatusCode.TooManyRequests => new(DeliveryDisposition.Retry, RetryAfter(response, timeProvider.GetUtcNow())),
                HttpStatusCode.RequestTimeout => new(DeliveryDisposition.Retry),
                >= HttpStatusCode.InternalServerError => new(DeliveryDisposition.Retry),
                _ => new(DeliveryDisposition.Rejected)
            };
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var retry = response.Headers.RetryAfter;
        var value = retry?.Delta ?? (retry?.Date is { } date ? date - now : null);
        return value is null ? null : TimeSpan.FromTicks(Math.Clamp(value.Value.Ticks, 0, MaximumRetryAfter.Ticks));
    }
}

internal sealed class DeliveryService(
    LatestSnapshotQueue queue,
    IngestClient client,
    CollectorOptions options,
    ILogger<DeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var snapshot = await queue.Read(stoppingToken);
            var stableBody = SnapshotContractV1.SerializeToUtf8Bytes(snapshot);
            for (var attempt = 1; attempt <= options.MaximumDeliveryAttempts; attempt++)
            {
                DeliveryResult result;
                try { result = await client.Send(stableBody, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (OperationCanceledException) { result = new(DeliveryDisposition.Retry); }
                catch (HttpRequestException) { result = new(DeliveryDisposition.Retry); }

                if (result.Disposition == DeliveryDisposition.Accepted)
                {
                    logger.LogInformation("Delivered snapshot sequence {Sequence}.", snapshot.Sequence);
                    break;
                }
                if (result.Disposition == DeliveryDisposition.Terminal)
                    throw new InvalidOperationException("Ingest rejected collector authentication or protocol compatibility.");
                if (result.Disposition == DeliveryDisposition.Rejected)
                {
                    logger.LogWarning("Ingest rejected snapshot sequence {Sequence}; moving to the latest snapshot.", snapshot.Sequence);
                    break;
                }
                if (attempt == options.MaximumDeliveryAttempts)
                {
                    logger.LogWarning("Delivery attempts exhausted for sequence {Sequence}; moving to the latest snapshot.", snapshot.Sequence);
                    break;
                }
                var exponential = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1)));
                var delay = result.RetryAfter ?? exponential + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
