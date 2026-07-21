using System.ComponentModel.DataAnnotations;

namespace Palmap.PalworldApi.Configuration;

internal sealed record PalworldApiSettings
{
    public const string SectionName = "PalworldApi";
    public const string ApiBase = "v1/api/";
    public const string HttpClientName = "PalworldApi";

    [Required]
    public Uri? BaseUrl { get; init; }

    [Required]
    public PalworldAdminSettings Admin { get; init; } = new();
}
