using Dashboard.Models;

namespace Dashboard.Services;

public interface INetworkService
{
    Task<NetworkDto> GetNetworkAsync(CancellationToken token = default);
}
