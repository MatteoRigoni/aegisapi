using Dashboard.Models;

namespace Dashboard.Services.Mock;

public class MockIncidentService : IIncidentService
{
    private readonly List<Incident> _incidents = new()
    {
        new Incident
        {
            Id = "1",
            Title = "Unauthorized Access",
            Status = "Open",
            Summary = "AI: suspicious login from unknown device detected",
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        },
        new Incident
        {
            Id = "2",
            Title = "Malware Detected",
            Status = "Investigating",
            Summary = "AI: malware signature matched (Trojan.X)",
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        },
        new Incident
        {
            Id = "3",
            Title = "Data Exfiltration",
            Status = "Mitigated",
            Summary = "AI: unusual outbound traffic blocked",
            CreatedAt = DateTime.UtcNow.AddHours(-4)
        }
    };

    public Task<IReadOnlyList<Incident>> GetIncidentsAsync() => Task.FromResult<IReadOnlyList<Incident>>(_incidents);

    public Task<Incident> GetIncidentAsync(string id) => Task.FromResult(_incidents.First(i => i.Id == id));
}
