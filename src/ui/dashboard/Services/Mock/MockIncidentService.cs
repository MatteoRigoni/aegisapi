using Dashboard.Models;

namespace Dashboard.Services.Mock;

public class MockIncidentService : IIncidentService
{
    private readonly List<Incident> _incidents = new()
    {
        new Incident { Id = "1", Title = "Unauthorized Access", Status = "Open", Summary = "AI: suspicious login detected", CreatedAt = DateTime.UtcNow.AddHours(-1) },
        new Incident { Id = "2", Title = "Malware Detected", Status = "Investigating", Summary = "AI: malware signature matched", CreatedAt = DateTime.UtcNow.AddHours(-2) }
    };

    public Task<IReadOnlyList<Incident>> GetIncidentsAsync() => Task.FromResult<IReadOnlyList<Incident>>(_incidents);

    public Task<Incident> GetIncidentAsync(string id) => Task.FromResult(_incidents.First(i => i.Id == id));
}
