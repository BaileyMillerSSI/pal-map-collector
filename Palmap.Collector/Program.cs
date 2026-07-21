using Palmap.Collector.Services;
using Palmap.CollectorApi;
using Palmap.PalworldApi;

namespace Palmap.Collector
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder
                .Build();

            app
                .AddPalworldApi()
                .AddCollectorApi();

            // Configure /healthz endpoint for docker host
            // Use C# native health checks API
            // Add PalMap.Api.Client health check using the associated Ping endpoint on the service
            AddCollectorBackgroundService(app);

            await app.RunAsync();
        }

        private static WebApplication AddCollectorBackgroundService(WebApplication builder)
        {
            // Configure TimedBackgroundService for PlayerLocationUpdate
            builder.Services.AddHostedService<PlayerLocationReporterTimedBackgroundService>();

            // Configure TimedBackgroundService for GameDataUpdate
            builder.Services.AddHostedService<GameDataReportTimedBackgroundService>();

            // Configure TimedBackgroundService for ServerSettingsUpdate
            builder.Services.AddHostedService<GameServerSettingsReportTimedBackgroundService>();

            return builder;
        }
    }
}
