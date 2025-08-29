using Dashboard.Models;

namespace Dashboard.Services.Real;

public class RealIncidentService : IIncidentService
{
    private readonly HttpClient _http;
    public RealIncidentService(HttpClient http) => _http = http;

    public Task<IReadOnlyList<Incident>> GetIncidentsAsync()
        => throw new NotImplementedException("Coming soon");

    public Task<Incident> GetIncidentAsync(string id)
        => throw new NotImplementedException("Coming soon");
}
