using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Palmap.PalworldApi.src.Configuration;
using Palmap.PalworldApi.src.Services;
using Palmap.PalworldApi.src.Services.Internal;
using Flurl;

namespace Palmap.PalworldApi
{
    public static class PalworldApiExtensions
    {
        public static IHostApplicationBuilder AddPalworldApi(this IHostApplicationBuilder builder)
        {
            // https://docs.palworldgame.com/api/rest-api/palwold-rest-api
            /// <see cref="Palmap.PalworldApi.src.Configuration.PalworldApiSettings"/> Configure this

            // Validate IOptions
            /*
             * Palworld API
             * PALWORLD_API_URL=
             * PALWORLD_API_ADMIN_PASSWORD=
             * PALWORLD_API_ADMIN_USERNAME=Admin
             * PALWORLD_API_PORT=8212
             * 
             */

            // Configure PalMap.PalworldApi.Client - HttpClientFactory -> Service creation with a pre-configured HttpClient ready to go for the API
            // Configure default safety checks, backoff, retry, etc.

            builder.Services.AddHttpClient<IPalworldApiService, PalworldApiService>((svp, client) =>
            {
                var settingsMonitor = svp.GetRequiredService<IOptionsMonitor<PalworldApiSettings>>();

                client.BaseAddress = settingsMonitor.CurrentValue.Url.AppendPathSegment(PalworldApiSettings.ApiBase).ToUri();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes(
                            $"{settingsMonitor.CurrentValue.Admin.Username}:{settingsMonitor.CurrentValue.Admin.Password}")));
            });

            return builder;
        }
    }
}
