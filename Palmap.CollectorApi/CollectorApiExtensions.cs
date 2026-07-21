using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Palmap.CollectorApi.src.Configuration;
using Palmap.CollectorApi.src.Services;
using Palmap.CollectorApi.src.Services.Internal;

namespace Palmap.CollectorApi
{
    public static class CollectorApiExtensions
    {
        public static IHostApplicationBuilder AddCollectorApi(this IHostApplicationBuilder builder)
        {
            /*
            * Collector Settings
            * 
            * COLLECTOR_API_URL=
            * COLLECTOR_API_CLIENT_SECRET=
            * COLLECTOR_API_CLIENT_ID=
            * COLLECTOR_PLAYER_LOCATIONUPDATEINTERVAL_MS=5000
            * COLLECTOR_GAME_DATAUPDATEINTERVAL_MS=30000
            * COLLECTOR_SERVER_SETTINGSUPDATEINTERVAL_MS=3600000
            * 
            */

            /// <see cref="Palmap.CollectorApi.src.Configuration.CollectorApiSettings"/> Configure this
            /// /// <see cref="Palmap.CollectorApi.src.Configuration.CollectorSettings"/> Configure this

            // Validate options

            // Configure PalMap.CollectorApi.Client - Configure a NoOpService for now
            // Configure default safety checks, backoff, retry, etc.

            builder.Services.AddHttpClient<ICollectorApiService, NoOpCollectorApiService>((svp, client) =>
            {
                var settingsMonitor = svp.GetRequiredService<IOptionsMonitor<CollectorApiSettings>>();

                client.BaseAddress = settingsMonitor.CurrentValue.Url;
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes(
                            $"{settingsMonitor.CurrentValue.ClientId}:{settingsMonitor.CurrentValue.ClientSecret}")));
            });

            return builder;
        }
    }
}
