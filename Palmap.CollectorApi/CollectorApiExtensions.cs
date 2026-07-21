using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Palmap.CollectorApi.Configuration;
using Palmap.CollectorApi.Services;
using Palmap.CollectorApi.Services.Internal;

namespace Palmap.CollectorApi;

public static class CollectorApiExtensions
{
    public static IHostApplicationBuilder AddCollectorApi(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<CollectorSettings>()
            .Bind(builder.Configuration.GetSection(CollectorSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<PalmapIngestSettings>()
            .Bind(builder.Configuration.GetSection(PalmapIngestSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<PalmapIngestSettings>, PalmapIngestSettingsValidator>();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SnapshotSanitizer>();
        builder.Services.AddSingleton<LatestSnapshotQueue>();
        builder.Services.AddSingleton<SnapshotCollectorApiService>();
        builder.Services.AddSingleton<ICollectorApiService>(services =>
            services.GetRequiredService<SnapshotCollectorApiService>());
        builder.Services.AddHttpClient(SnapshotDeliveryService.HttpClientName, client =>
            client.Timeout = Timeout.InfiniteTimeSpan);
        builder.Services.AddHostedService<SnapshotDeliveryService>();
        return builder;
    }
}
