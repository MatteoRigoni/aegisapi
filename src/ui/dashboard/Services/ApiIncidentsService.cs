namespace Dashboard.Services;

public class ApiIncidentsService : IIncidentsService
{
    public Task<IEnumerable<Incident>> GetIncidentsAsync()
        => Task.FromResult<IEnumerable<Incident>>(Array.Empty<Incident>());

    public Task<Incident?> GetIncidentAsync(string id)
        => Task.FromResult<Incident?>(null);
}
