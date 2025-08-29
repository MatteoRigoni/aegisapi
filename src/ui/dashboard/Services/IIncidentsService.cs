namespace Dashboard.Services;

public interface IIncidentsService
{
    Task<IEnumerable<Incident>> GetIncidentsAsync();
    Task<Incident?> GetIncidentAsync(string id);
}
