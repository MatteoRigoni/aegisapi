using Dashboard.Models;

namespace Dashboard.Services.Mock;

public class MockNetworkService : INetworkService
{
    public Task<NetworkGraphDto> GetNetworkAsync()
    {
        var nodes = new[]
        {
            new NetworkNodeDto("gateway", "Gateway", 10, 120),
            new NetworkNodeDto("api", "API", 25, 80),
            new NetworkNodeDto("db", "Database", 40, 40)
        };
        var edges = new[]
        {
            new NetworkEdgeDto("gateway", "api", 80, 25),
            new NetworkEdgeDto("api", "db", 40, 40)
        };
        return Task.FromResult(new NetworkGraphDto(nodes, edges));
    }
}
