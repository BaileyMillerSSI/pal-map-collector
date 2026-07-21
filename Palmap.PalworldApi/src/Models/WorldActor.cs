using System.Text.Json.Serialization;

namespace Palmap.PalworldApi.Models;

// Character and PalBox entries share one wire collection. Optional members avoid
// coupling deserialization to a brittle polymorphic discriminator.
internal sealed record WorldActor
{
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("InstanceID")]
    public string? InstanceId { get; init; }

    public string? UnitType { get; init; }

    public string? NickName { get; init; }

    [JsonPropertyName("TrainerInstanceID")]
    public string? TrainerInstanceId { get; init; }

    public string? TrainerNickName { get; init; }

    public string? TrainerClass { get; init; }

    [JsonPropertyName("userid")]
    public string? UserId { get; init; }

    [JsonPropertyName("ip")]
    public string? IpAddress { get; init; }

    [JsonPropertyName("level")]
    public int? Level { get; init; }

    [JsonPropertyName("HP")]
    public int? HitPoints { get; init; }

    [JsonPropertyName("MaxHP")]
    public int? MaxHitPoints { get; init; }

    [JsonPropertyName("GuildID")]
    public string? GuildId { get; init; }

    public string? GuildName { get; init; }

    public string? Class { get; init; }

    public string? Action { get; init; }

    [JsonPropertyName("AI_Action")]
    public string? AiAction { get; init; }

    public float? LocationX { get; init; }

    public float? LocationY { get; init; }

    public float? LocationZ { get; init; }

    public float? RotationX { get; init; }

    public float? RotationY { get; init; }

    public float? RotationZ { get; init; }

    public string? Stage { get; init; }

    // The current REST schema represents this value as the strings "true" and "false".
    public string? IsActive { get; init; }
}
