using System.Net.Http;
using System.Threading.Tasks;

namespace Gateway.E2ETests;

public class ProxyTests : IClassFixture<GatewayFactory>
{
    private readonly HttpClient _client;

    public ProxyTests(GatewayFactory factory)
    {
        _client = factory.Client;
    }

    [Fact]
    public async Task Gateway_Proxies_To_Backend()
    {
        var response = await _client.GetStringAsync("/api/hello");
        Assert.Equal("world", response);
    }
}

