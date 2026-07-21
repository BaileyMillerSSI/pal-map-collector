using Palmap.PalworldApi.Configuration;

namespace Palmap.PalworldApi.Services.Internal;

internal sealed class PalworldApiServiceFactory(IHttpClientFactory httpClientFactory) : IPalworldApiServiceFactory
{
    public IPalworldApiService Create() => new PalworldApiService(
        httpClientFactory.CreateClient(PalworldApiSettings.HttpClientName));
}
