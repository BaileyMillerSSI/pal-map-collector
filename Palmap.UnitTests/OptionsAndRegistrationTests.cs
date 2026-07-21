using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text;
using Palmap.CollectorApi;
using Palmap.CollectorApi.Configuration;
using Palmap.PalworldApi;
using Palmap.PalworldApi.Configuration;
using Palmap.PalworldApi.Services;

namespace Palmap.UnitTests;

public sealed class OptionsAndRegistrationTests
{
    [Fact]
    public void PalworldOptionsBindAndConfigureHttpClient()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PalworldApi:BaseUrl"] = "http://palworld:8212",
            ["PalworldApi:Admin:Username"] = "admin",
            ["PalworldApi:Admin:Password"] = "sëcret"
        });
        builder.AddPalworldApi();
        using var provider = builder.Services.BuildServiceProvider();

        var settings = provider.GetRequiredService<IOptions<PalworldApiSettings>>().Value;
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(PalworldApiSettings.HttpClientName);
        var serviceFactory = provider.GetRequiredService<IPalworldApiServiceFactory>();
        using var firstService = serviceFactory.Create();
        using var secondService = serviceFactory.Create();

        Assert.Equal(new Uri("http://palworld:8212"), settings.BaseUrl);
        Assert.Equal(new Uri("http://palworld:8212/v1/api/"), client.BaseAddress);
        Assert.Equal("Basic", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:sëcret")),
            client.DefaultRequestHeaders.Authorization?.Parameter);
        Assert.NotSame(firstService, secondService);
    }

    [Theory]
    [InlineData("relative/path", "secret")]
    [InlineData("ftp://palworld", "secret")]
    [InlineData("http://palworld:8212", "")]
    public void InvalidPalworldOptionsFailValidation(string url, string password)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PalworldApi:BaseUrl"] = url,
            ["PalworldApi:Admin:Username"] = "admin",
            ["PalworldApi:Admin:Password"] = password
        });
        builder.AddPalworldApi();
        using var provider = builder.Services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<PalworldApiSettings>>().Value);
    }

    [Theory]
    [InlineData("PlayerLocationUpdateIntervalMs")]
    [InlineData("GameDataUpdateIntervalMs")]
    [InlineData("ServerSettingsUpdateIntervalMs")]
    [InlineData("FailureRetryIntervalMs")]
    [InlineData("PalworldHealthCacheDurationMs")]
    public void CollectorIntervalsMustBePositive(string settingName)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"Collector:{settingName}"] = "0";
        builder.AddCollectorApi();
        using var provider = builder.Services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CollectorSettings>>().Value);
    }
}
