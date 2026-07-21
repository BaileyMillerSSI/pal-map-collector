namespace Palmap.PalworldApi.Models;

internal sealed record WorldActorSnapshotResponse
{
    // Palworld returns server-local time without an offset.
    public string Time { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("FPS")]
    public float Fps { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("AverageFPS")]
    public float AverageFps { get; init; }

    public IReadOnlyList<WorldActor> ActorData { get; init; } = [];
}
