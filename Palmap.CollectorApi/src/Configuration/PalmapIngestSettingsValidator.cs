using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Palmap.CollectorApi.Configuration;

internal sealed class PalmapIngestSettingsValidator(IHostEnvironment environment)
    : IValidateOptions<PalmapIngestSettings>
{
    private static readonly Regex ClientIdPattern = new(
        @"^pmc_[A-Za-z0-9_-]{16,60}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex ClientSecretPattern = new(
        @"^[A-Za-z0-9_-]{32,128}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public ValidateOptionsResult Validate(string? name, PalmapIngestSettings settings)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("PalmapIngest:Endpoint must be an absolute HTTP or HTTPS URL.");
        }
        else
        {
            if (!string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                errors.Add("PalmapIngest:Endpoint cannot contain user information, a query, or a fragment.");
            }

            if (endpoint.Scheme == Uri.UriSchemeHttp &&
                !(environment.IsDevelopment() && settings.AllowInsecureHttp))
            {
                errors.Add("An HTTP PalmapIngest:Endpoint requires Development and PalmapIngest:AllowInsecureHttp=true.");
            }
        }

        if (!ClientIdPattern.IsMatch(settings.ClientId ?? string.Empty))
        {
            errors.Add("PalmapIngest:ClientId must contain 20 to 64 characters total: 'pmc_' followed by 16 to 60 base64url characters.");
        }

        if (!ClientSecretPattern.IsMatch(settings.ClientSecret ?? string.Empty))
        {
            errors.Add("PalmapIngest:ClientSecret must contain 32 to 128 base64url characters and cannot contain a colon.");
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
