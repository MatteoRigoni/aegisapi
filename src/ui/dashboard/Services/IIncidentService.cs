using Dashboard.Models;

namespace Dashboard.Services;

public interface IIncidentService
{
    Task<IReadOnlyList<Incident>> GetIncidentsAsync();
    Task<Incident> GetIncidentAsync(string id);
}
