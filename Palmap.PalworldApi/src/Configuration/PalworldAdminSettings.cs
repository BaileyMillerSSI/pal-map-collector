using System.ComponentModel.DataAnnotations;

namespace Palmap.PalworldApi.Configuration;

internal sealed record PalworldAdminSettings
{
    [Required]
    public string Username { get; init; } = "admin";

    [Required]
    public string Password { get; init; } = string.Empty;
}
