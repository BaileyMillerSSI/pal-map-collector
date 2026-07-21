using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        builder.Services.AddSingleton<ICollectorApiService, NoOpCollectorApiService>();
        return builder;
    }
}
