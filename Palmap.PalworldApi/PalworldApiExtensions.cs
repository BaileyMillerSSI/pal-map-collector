using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Palmap.PalworldApi.Configuration;
using Palmap.PalworldApi.Services;
using Palmap.PalworldApi.Services.Internal;

namespace Palmap.PalworldApi;

public static class PalworldApiExtensions
{
    public static IHostApplicationBuilder AddPalworldApi(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<PalworldApiSettings>()
            .Bind(builder.Configuration.GetSection(PalworldApiSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<PalworldApiSettings>, PalworldApiSettingsValidator>();

        builder.Services
            .AddHttpClient(PalworldApiSettings.HttpClientName, (serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptionsMonitor<PalworldApiSettings>>().CurrentValue;
                client.BaseAddress = new Uri(settings.BaseUrl!, PalworldApiSettings.ApiBase);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes(
                            $"{settings.Admin.Username}:{settings.Admin.Password}")));
            })
            .AddStandardResilienceHandler();
        builder.Services.AddSingleton<IPalworldApiServiceFactory, PalworldApiServiceFactory>();

        return builder;
    }
}
