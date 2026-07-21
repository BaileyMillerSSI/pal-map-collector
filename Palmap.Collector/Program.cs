using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Palmap.Collector.Health;
using Palmap.Collector.Services;
using Palmap.CollectorApi;
using Palmap.PalworldApi;
using Serilog;

namespace Palmap.Collector;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSerilog((services, configuration) => configuration
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services));

            builder
                .AddPalworldApi()
                .AddCollectorApi();

            AddCollectorBackgroundServices(builder.Services);
            builder.Services
                .AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
                .AddCheck<PalworldApiHealthCheck>("palworld-api", tags: ["ready"]);

            var app = builder.Build();
            app.UseSerilogRequestLogging();
            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live")
            });
            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready")
            });

            await app.RunAsync();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Palmap Collector terminated unexpectedly.");
            throw;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void AddCollectorBackgroundServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICollectorDelay, CollectorDelay>();
        services.AddSingleton<IPalworldApiHealthService, PalworldApiHealthService>();
        services.AddHostedService<PlayerLocationReporterTimedBackgroundService>();
        services.AddHostedService<GameDataReportTimedBackgroundService>();
        services.AddHostedService<GameServerSettingsReportTimedBackgroundService>();
    }
}
