namespace Palmap.PalworldApi.src.Services.Internal
{
    /// <summary>
    /// https://docs.palworldgame.com/api/rest-api/palwold-rest-api
    /// </summary>
    /// <param name="apiClient"></param>
    internal class PalworldApiService(HttpClient apiClient): IPalworldApiService
    {
        private readonly HttpClient _apiClient = apiClient;

        public Task<object> PlayerList()
        {
            throw new NotImplementedException();
        }

        public Task<object> ServerInfo()
        {
            throw new NotImplementedException();
        }

        public Task<object> ServerMetrics()
        {
            throw new NotImplementedException();
        }

        public Task<object> ServerSettings()
        {
            throw new NotImplementedException();
        }

        public Task<object> WorldActorSnapshot()
        {
            throw new NotImplementedException();
        }

        public async Task<bool> Ping()
        {
            var result = await _apiClient.GetAsync("/info");

            return result.IsSuccessStatusCode;
        }
    }
}
