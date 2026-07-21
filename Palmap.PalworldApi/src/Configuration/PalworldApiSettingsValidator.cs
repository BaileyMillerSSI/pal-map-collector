using Microsoft.Extensions.Options;

namespace Palmap.PalworldApi.Configuration;

internal sealed class PalworldApiSettingsValidator : IValidateOptions<PalworldApiSettings>
{
    public ValidateOptionsResult Validate(string? name, PalworldApiSettings options)
    {
        if (options.BaseUrl is null || !options.BaseUrl.IsAbsoluteUri)
        {
            return ValidateOptionsResult.Fail("PalworldApi:BaseUrl must be an absolute URI.");
        }

        if (!string.Equals(options.BaseUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.BaseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("PalworldApi:BaseUrl must use HTTP or HTTPS.");
        }

        if (string.IsNullOrWhiteSpace(options.Admin.Username))
        {
            return ValidateOptionsResult.Fail("PalworldApi:Admin:Username is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Admin.Password))
        {
            return ValidateOptionsResult.Fail("PalworldApi:Admin:Password is required.");
        }

        return ValidateOptionsResult.Success;
    }
}
