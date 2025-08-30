using Dashboard.Models;

namespace Dashboard.Services;

public interface INetworkService
{
    Task<NetworkGraphDto> GetNetworkAsync();
}
