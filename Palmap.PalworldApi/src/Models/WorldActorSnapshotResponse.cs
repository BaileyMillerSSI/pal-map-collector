using System.Text.Json;

namespace Palmap.PalworldApi.Models;

internal sealed record WorldActorSnapshotResponse
{
    // Palworld returns server-local time without an offset.
    public string Time { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("FPS")]
    public double Fps { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("AverageFPS")]
    public double AverageFps { get; init; }

    public double? InGameDays { get; init; }

    public JsonElement? InGameTime { get; init; }

    public IReadOnlyList<WorldActor> ActorData { get; init; } = [];
}
