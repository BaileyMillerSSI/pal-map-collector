using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Palmap.Collector.Health;
using Palmap.Collector.Logging;
using Palmap.Collector.Services;
using Palmap.CollectorApi;
using Palmap.PalworldApi;
using Serilog;
using Serilog.Events;

namespace Palmap.Collector;

internal static class Program
{
    internal const string StartedMessage =
        "Pal-Map Collector started at {LogLevel}; Palworld polling and snapshot delivery are active.";
    internal const string StoppingMessage =
        "Pal-Map Collector is shutting down; polling and delivery are stopping.";
    internal const string FatalMessage =
        "Pal-Map Collector stopped and snapshots will not be delivered ({ExceptionType}). " +
        "Review the configuration and preceding log messages before restarting.";

    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            var logLevel = CollectorLogLevel.Parse(builder.Configuration["LogLevel"]);
            Log.Logger = logLevel.Apply(new LoggerConfiguration()
                    .WriteTo.Console())
                .CreateBootstrapLogger();

            builder.Services.AddSerilog((services, configuration) =>
            {
                configuration.ReadFrom.Configuration(builder.Configuration)
                    .ReadFrom.Services(services);
                logLevel.Apply(configuration);
            });

            builder
                .AddPalworldApi()
                .AddCollectorApi();

            AddCollectorBackgroundServices(builder.Services);
            builder.Services
                .AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
                .AddCheck<PalworldApiHealthCheck>("palworld-api", tags: ["ready"]);

            var app = builder.Build();
            app.UseSerilogRequestLogging(options =>
            {
                options.GetLevel = (_, _, exception) =>
                    exception is null
                        ? LogEventLevel.Debug
                        : LogEventLevel.Error;
            });
            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live")
            });
            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready")
            });
            app.Lifetime.ApplicationStarted.Register(() =>
                Log.Information(StartedMessage, logLevel.Name));
            app.Lifetime.ApplicationStopping.Register(() =>
                Log.Information(StoppingMessage));

            await app.RunAsync();
        }
        catch (Exception exception)
        {
            Log.Fatal(FatalMessage, exception.GetType().Name);
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
