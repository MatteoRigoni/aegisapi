using System.Net.Http.Json;
using Dashboard.Models;

namespace Dashboard.Services.Real;

public class RealSummarizerService : ISummarizerService
{
    private readonly HttpClient _http;

    public RealSummarizerService(HttpClient http)
    {
        _http = http;
    }

    public async Task<SummaryResponse?> SummarizeAsync(IncidentBundle bundle, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/ai/summarize", bundle, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<SummaryResponse>(cancellationToken: ct);
    }
}
