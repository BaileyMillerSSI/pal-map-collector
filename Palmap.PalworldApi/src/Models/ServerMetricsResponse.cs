using System.Text.Json.Serialization;

namespace Palmap.PalworldApi.Models;

internal sealed record ServerMetricsResponse
{
    [JsonPropertyName("serverfps")]
    public int ServerFps { get; init; }

    [JsonPropertyName("currentplayernum")]
    public int CurrentPlayerCount { get; init; }

    [JsonPropertyName("serverframetime")]
    public double ServerFrameTimeMilliseconds { get; init; }

    [JsonPropertyName("maxplayernum")]
    public int MaxPlayerCount { get; init; }

    [JsonPropertyName("uptime")]
    public long UptimeSeconds { get; init; }

    [JsonPropertyName("basecampnum")]
    public int BaseCampCount { get; init; }

    [JsonPropertyName("days")]
    public int Days { get; init; }
}
