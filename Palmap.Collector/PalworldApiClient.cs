using System.Net.Http.Json;

namespace Palmap.Collector;

internal sealed class PalworldApiClient(HttpClient client)
{
    public Task<PalworldPlayerList> Players(CancellationToken token) => Get<PalworldPlayerList>("players", token);
    public Task<PalworldWorldSnapshot> World(CancellationToken token) => Get<PalworldWorldSnapshot>("game-data", token);
    public Task<PalworldSettings> Settings(CancellationToken token) => Get<PalworldSettings>("settings", token);

    private async Task<T> Get<T>(string path, CancellationToken token)
    {
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: token)
            ?? throw new HttpRequestException($"Palworld endpoint '{path}' returned no JSON document.");
    }
}
