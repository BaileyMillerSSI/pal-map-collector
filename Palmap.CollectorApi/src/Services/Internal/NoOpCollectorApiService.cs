namespace Palmap.CollectorApi.src.Services.Internal
{
    internal class NoOpCollectorApiService(HttpClient apiClient) : ICollectorApiService
    {
        private readonly HttpClient _apiClient = apiClient;

        public Task<object> ReportPlayerLocations()
        {
            throw new NotImplementedException();
        }

        public Task<object> ReportGameData()
        {
            throw new NotImplementedException();
        }

        public Task<object> ReportServerSettings()
        {
            throw new NotImplementedException();
        }

        public Task<object> Authenticate()
        {
            throw new NotImplementedException();
        }
    }
}
