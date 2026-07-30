using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using Palmap.Collector.Configuration;
using Palmap.Collector.Metrics;
using Palmap.Collector.Services;
using Palmap.CollectorApi.Metrics;

namespace Palmap.Collector;

internal static class PrometheusExporterExtensions
{
    public static IHostApplicationBuilder AddPrometheusExporter(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<PrometheusExporterSettings>()
            .Bind(builder.Configuration.GetSection(PrometheusExporterSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var enabled = builder.Configuration.GetSection(PrometheusExporterSettings.SectionName)
            .GetValue(nameof(PrometheusExporterSettings.Enabled), false);
        if (!enabled)
        {
            return builder;
        }

        var section = builder.Configuration.GetSection(PrometheusExporterSettings.SectionName);
        var host = section[nameof(PrometheusExporterSettings.Host)] ?? "+";
        var port = section.GetValue(nameof(PrometheusExporterSettings.Port), 9090);

        builder.Services.AddSingleton<PalworldMetricsCache>();
        builder.Services.AddSingleton<CollectorObservableMetrics>();
        builder.Services.AddHostedService<PalworldMetricsSampler>();
        builder.Services.AddHostedService<CollectorObservableMetricsRegistration>();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(CollectorMetrics.MeterName);
                metrics.AddPrometheusHttpListener(options =>
                {
                    options.Host = host;
                    options.Port = port;
                });
            });
        return builder;
    }
}

internal sealed class CollectorObservableMetricsRegistration(CollectorObservableMetrics observables)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        observables.Register();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
