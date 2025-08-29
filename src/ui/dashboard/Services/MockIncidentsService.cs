namespace Dashboard.Services;

public class MockIncidentsService : IIncidentsService
{
    private readonly List<Incident> _incidents = new()
    {
        new Incident { Id = "1", Title = "SQL Injection", Severity = "High", Description = "Potential SQL injection detected", AiSummary = "Input sanitization recommended." },
        new Incident { Id = "2", Title = "XSS Attempt", Severity = "Medium", Description = "Cross-site scripting attempt detected", AiSummary = "Ensure output encoding." }
    };

    public Task<IEnumerable<Incident>> GetIncidentsAsync() => Task.FromResult<IEnumerable<Incident>>(_incidents);

    public Task<Incident?> GetIncidentAsync(string id) => Task.FromResult(_incidents.FirstOrDefault(i => i.Id == id));
}
