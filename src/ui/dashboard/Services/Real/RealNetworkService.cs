using System.Net.Http.Json;
using Dashboard.Models;

namespace Dashboard.Services.Real;

public class RealNetworkService : INetworkService
{
    private readonly HttpClient _http;
    public RealNetworkService(HttpClient http) => _http = http;

    public async Task<NetworkGraphDto> GetNetworkAsync()
        => await _http.GetFromJsonAsync<NetworkGraphDto>("network/map")
            ?? new NetworkGraphDto(Array.Empty<NetworkNodeDto>(), Array.Empty<NetworkEdgeDto>());
}
