using System.Text.Json.Serialization;

namespace Palmap.PalworldApi.Models;

internal sealed record PlayerListResponse
{
    [JsonPropertyName("players")]
    public IReadOnlyList<PalworldPlayer> Players { get; init; } = [];
}
