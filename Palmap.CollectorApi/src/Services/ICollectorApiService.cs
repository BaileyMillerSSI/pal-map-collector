namespace Palmap.CollectorApi.src.Services
{
    public interface ICollectorApiService
    {
        Task<object> ReportPlayerLocations();

        Task<object> ReportGameData();

        Task<object> ReportServerSettings();
    }
}
