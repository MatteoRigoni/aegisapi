using Dashboard.Models;

namespace Dashboard.Services;

public interface ISummarizerService
{
    Task<SummaryResponse?> SummarizeAsync(IncidentBundle bundle, CancellationToken ct = default);
}
