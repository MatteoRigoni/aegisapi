using Dashboard.Models;

namespace Dashboard.Services.Mock;

public class MockSummarizerService : ISummarizerService
{
    public Task<SummaryResponse?> SummarizeAsync(IncidentBundle bundle, CancellationToken ct = default)
    {
        var patch = new PolicyPatch("mock", "/", "allow", new Dictionary<string,string>());
        var summary = new SummaryResponse("Mock summary", "Mock cause", patch, 1.0, Array.Empty<string>());
        return Task.FromResult<SummaryResponse?>(summary);
    }
}
