using Dashboard.Models;

namespace Dashboard.Services.Mock;

public class MockNetworkService : INetworkService
{
    public Task<NetworkDto> GetNetworkAsync(CancellationToken token = default)
    {
        var nodes = new[]
        {
            new NetworkNodeDto("gateway", "Gateway", 200, 5),
            new NetworkNodeDto("service-a", "Service A", 120, 30),
            new NetworkNodeDto("service-b", "Service B", 80, 45)
        };
        var links = new[]
        {
            new NetworkLinkDto("gateway", "service-a", 120, 30),
            new NetworkLinkDto("gateway", "service-b", 80, 45)
        };
        return Task.FromResult(new NetworkDto(nodes, links));
    }
}
