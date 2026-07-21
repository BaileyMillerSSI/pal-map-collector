using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Palmap.Collector;

if (args.Contains("--health-check", StringComparer.Ordinal))
{
    return HeartbeatService.IsHealthy(CollectorOptions.DefaultHeartbeatPath) ? 0 : 1;
}

var builder = Host.CreateApplicationBuilder(args);
var options = CollectorOptions.Load(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PrivacyTransformer>();
builder.Services.AddSingleton<SnapshotState>();
builder.Services.AddSingleton<LatestSnapshotQueue>();
builder.Services.AddHttpClient<PalworldApiClient>(client =>
{
    client.BaseAddress = new Uri(options.PalworldApiUrl, "/v1/api/");
    client.Timeout = options.RequestTimeout;
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.Authorization = BasicAuthentication.Create(
        options.PalworldAdminUsername,
        options.PalworldAdminPassword);
});
builder.Services.AddHttpClient<IngestClient>(client =>
{
    client.BaseAddress = options.PalmapIngestUrl;
    client.Timeout = options.RequestTimeout;
    client.DefaultRequestHeaders.Authorization = BasicAuthentication.Create(
        options.PalmapClientId,
        options.PalmapClientSecret);
});
builder.Services.AddHostedService<PlayersPollingService>();
builder.Services.AddHostedService<WorldPollingService>();
builder.Services.AddHostedService<SettingsPollingService>();
builder.Services.AddHostedService<DeliveryService>();
builder.Services.AddHostedService<HeartbeatService>();
builder.Services.Configure<HostOptions>(host =>
    host.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

await builder.Build().RunAsync();
return 0;
