using System.Text.Json.Serialization;

namespace Palmap.PalworldApi.Models;

internal sealed record PalworldPlayer
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("accountName")]
    public string AccountName { get; init; } = string.Empty;

    [JsonPropertyName("playerId")]
    public string PlayerId { get; init; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("ip")]
    public string IpAddress { get; init; } = string.Empty;

    [JsonPropertyName("ping")]
    public double Ping { get; init; }

    [JsonPropertyName("location_x")]
    public double LocationX { get; init; }

    [JsonPropertyName("location_y")]
    public double LocationY { get; init; }

    [JsonPropertyName("level")]
    public int Level { get; init; }

    [JsonPropertyName("building_count")]
    public int BuildingCount { get; init; }
}
