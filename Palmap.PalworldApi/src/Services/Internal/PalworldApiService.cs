using System.Net.Http.Json;
using Palmap.PalworldApi.Models;
using Polly.Timeout;

namespace Palmap.PalworldApi.Services.Internal;

internal sealed class PalworldApiService(HttpClient apiClient) : IPalworldApiService
{
    public Task<ServerInfoResponse> ServerInfo(CancellationToken cancellationToken = default) =>
        Get<ServerInfoResponse>("info", cancellationToken);

    public Task<PlayerListResponse> PlayerList(CancellationToken cancellationToken = default) =>
        Get<PlayerListResponse>("players", cancellationToken);

    public Task<ServerSettingsResponse> ServerSettings(CancellationToken cancellationToken = default) =>
        Get<ServerSettingsResponse>("settings", cancellationToken);

    public Task<WorldActorSnapshotResponse> WorldActorSnapshot(CancellationToken cancellationToken = default) =>
        Get<WorldActorSnapshotResponse>("game-data", cancellationToken);

    public Task<ServerMetricsResponse> ServerMetrics(CancellationToken cancellationToken = default) =>
        Get<ServerMetricsResponse>("metrics", cancellationToken);

    public async Task<bool> Ping(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await apiClient.GetAsync("info", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (TimeoutRejectedException)
        {
            return false;
        }
    }

    public void Dispose() => apiClient.Dispose();

    private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await apiClient.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                ?? throw new HttpRequestException($"Palworld REST endpoint '{path}' returned an empty response body.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"Palworld REST endpoint '{path}' timed out.", exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new HttpRequestException($"Palworld REST endpoint '{path}' timed out.", exception);
        }
    }
}
