namespace Palmap.PalworldApi.src.Services
{
    public interface IPalworldApiService
    {
        Task<object> ServerInfo();

        Task<object> PlayerList();

        Task<object> ServerSettings();

        Task<object> WorldActorSnapshot();

        Task<object> ServerMetrics();

        Task<bool> Ping();
    }
}
