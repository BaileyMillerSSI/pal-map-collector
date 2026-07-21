using System.Text.Json.Serialization;

namespace Palmap.PalworldApi.Models;

internal sealed record ServerInfoResponse
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("servername")]
    public string ServerName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("worldguid")]
    public string WorldGuid { get; init; } = string.Empty;
}
