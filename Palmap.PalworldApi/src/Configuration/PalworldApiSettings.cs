namespace Palmap.PalworldApi.src.Configuration
{

    internal record PalworldApiSettings
    {
        public string? Url { get; init; } = string.Empty;

        public const string ApiBase = "/v1/api";

        public uint Port { get; init; } = 8212;

        public PalworldAdminSettings Admin { get; init; } = new PalworldAdminSettings();


    }

    internal record PalworldAdminSettings
    {
        public string Username { get; init; } = "Admin";

        public string Password { get; init; } = string.Empty;
    }
}
