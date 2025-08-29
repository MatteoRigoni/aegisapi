using Dashboard.Models;

namespace Dashboard.Services.Real;

public class RealMetricsService : IMetricsService
{
    private readonly HttpClient _http;
    public RealMetricsService(HttpClient http) => _http = http;

    public Task StartMetricsAsync(Func<MetricDto, Task> handler, CancellationToken token)
        => throw new NotImplementedException("Coming soon");
}
