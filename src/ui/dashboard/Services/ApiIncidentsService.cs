namespace Dashboard.Services;

public class ApiIncidentsService : IIncidentsService
{
    public Task<IEnumerable<Incident>> GetIncidentsAsync() => throw new NotImplementedException();
    public Task<Incident?> GetIncidentAsync(string id) => throw new NotImplementedException();
}
