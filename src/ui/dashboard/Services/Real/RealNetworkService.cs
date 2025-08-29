using Dashboard.Models;

namespace Dashboard.Services.Real;

public class RealNetworkService : INetworkService
{
    private readonly HttpClient _http;
    public RealNetworkService(HttpClient http) => _http = http;

    public async Task<NetworkDto> GetNetworkAsync(CancellationToken token = default)
        => await _http.GetFromJsonAsync<NetworkDto>("network", token)
            ?? new NetworkDto(Array.Empty<NetworkNodeDto>(), Array.Empty<NetworkLinkDto>());
}
