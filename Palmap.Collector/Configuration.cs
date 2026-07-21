using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Palmap.Collector;

internal sealed record CollectorOptions
{
    public static string DefaultHeartbeatPath => Path.Combine(Path.GetTempPath(), "palmap-collector.heartbeat");

    public required Uri PalworldApiUrl { get; init; }
    public required string PalworldAdminUsername { get; init; }
    public required string PalworldAdminPassword { get; init; }
    public required Uri PalmapIngestUrl { get; init; }
    public required string PalmapClientId { get; init; }
    public required string PalmapClientSecret { get; init; }
    public required byte[] PrivacyKey { get; init; }
    public TimeSpan PlayersInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan WorldInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan SettingsInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan FailureInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public int MaximumDeliveryAttempts { get; init; } = 5;
    public string HeartbeatPath { get; init; } = DefaultHeartbeatPath;

    public static CollectorOptions Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var palworldUrl = RequiredUri(configuration, "PALWORLD_API_URL");
        if (palworldUrl.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("PALWORLD_API_URL must use HTTP or HTTPS.");

        var ingestUrl = RequiredUri(configuration, "PALMAP_INGEST_URL");
        var allowInsecure = bool.TryParse(configuration["PALMAP_ALLOW_INSECURE_INGEST"], out var enabled) && enabled;
        if (ingestUrl.Scheme != Uri.UriSchemeHttps &&
            !(ingestUrl.Scheme == Uri.UriSchemeHttp && allowInsecure && environment.IsDevelopment()))
        {
            throw new InvalidOperationException(
                "PALMAP_INGEST_URL must use HTTPS. HTTP requires Development and PALMAP_ALLOW_INSECURE_INGEST=true.");
        }

        return new CollectorOptions
        {
            PalworldApiUrl = palworldUrl,
            PalworldAdminUsername = Required(configuration, "PALWORLD_ADMIN_USERNAME"),
            PalworldAdminPassword = Secret(configuration, "PALWORLD_ADMIN_PASSWORD"),
            PalmapIngestUrl = ingestUrl,
            PalmapClientId = Required(configuration, "PALMAP_CLIENT_ID"),
            PalmapClientSecret = Secret(configuration, "PALMAP_CLIENT_SECRET"),
            PrivacyKey = ParsePrivacyKey(Secret(configuration, "PALMAP_PRIVACY_KEY"))
        };
    }

    private static Uri RequiredUri(IConfiguration configuration, string key)
    {
        var value = Required(configuration, key);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"{key} must be an absolute URL.");
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"{key} is required.");

    private static string Secret(IConfiguration configuration, string key)
    {
        var direct = configuration[key];
        var file = configuration[$"{key}_FILE"];
        if (!string.IsNullOrEmpty(direct) && !string.IsNullOrEmpty(file))
            throw new InvalidOperationException($"Set either {key} or {key}_FILE, not both.");
        if (!string.IsNullOrEmpty(direct)) return direct;
        if (string.IsNullOrWhiteSpace(file)) throw new InvalidOperationException($"{key} or {key}_FILE is required.");
        try
        {
            var value = File.ReadAllText(file).TrimEnd('\r', '\n');
            return value.Length > 0 ? value : throw new InvalidOperationException($"{key}_FILE was empty.");
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Unable to read {key}_FILE.", exception);
        }
    }

    private static byte[] ParsePrivacyKey(string value)
    {
        byte[] key;
        try { key = Convert.FromBase64String(value); }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("PALMAP_PRIVACY_KEY must be base64-encoded.", exception);
        }
        return key.Length == 32
            ? key
            : throw new InvalidOperationException("PALMAP_PRIVACY_KEY must contain exactly 256 bits.");
    }
}

internal static class BasicAuthentication
{
    public static AuthenticationHeaderValue Create(string username, string password)
    {
        if (username.Contains(':', StringComparison.Ordinal))
            throw new InvalidOperationException("Basic authentication usernames cannot contain a colon.");
        return new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
    }
}
