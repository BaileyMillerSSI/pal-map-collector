using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Palmap.CollectorApi.Configuration;

internal sealed class PalmapIngestSettingsValidator(IHostEnvironment environment)
    : IValidateOptions<PalmapIngestSettings>
{
    public ValidateOptionsResult Validate(string? name, PalmapIngestSettings settings)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("PalmapIngest:Endpoint must be an absolute HTTP or HTTPS URL.");
        }
        else if (endpoint.Scheme == Uri.UriSchemeHttp &&
            !(environment.IsDevelopment() && settings.AllowInsecureHttp))
        {
            errors.Add("An HTTP PalmapIngest:Endpoint requires Development and PalmapIngest:AllowInsecureHttp=true.");
        }

        if (settings.ClientId?.Contains(':', StringComparison.Ordinal) == true)
        {
            errors.Add("PalmapIngest:ClientId cannot contain a colon.");
        }

        if (!TryDecodePrivacyKey(settings.PrivacyKey, out var key) || key.Length != 32)
        {
            errors.Add("PalmapIngest:PrivacyKey must be base64 containing exactly 256 bits.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    internal static byte[] DecodePrivacyKey(string value) =>
        Convert.FromBase64String(value);

    private static bool TryDecodePrivacyKey(string? value, out byte[] key)
    {
        try
        {
            key = string.IsNullOrWhiteSpace(value) ? [] : Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }
}
