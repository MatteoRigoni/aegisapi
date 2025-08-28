using System.Net.Http.Json;

namespace Gateway.AI;

public interface ISummarizerClient
{
    Task<SummaryResponse?> SummarizeAsync(IncidentBundle bundle, CancellationToken ct = default);
}

public sealed class SummarizerHttpClient(HttpClient http) : ISummarizerClient
{
    public async Task<SummaryResponse?> SummarizeAsync(IncidentBundle bundle, CancellationToken ct = default)
    {
        var res = await http.PostAsJsonAsync("/ai/summarize", bundle, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<SummaryResponse>(cancellationToken: ct);
    }
}
