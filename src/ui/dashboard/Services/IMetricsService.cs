using Dashboard.Models;

namespace Dashboard.Services;

public interface IMetricsService
{
    Task StartMetricsAsync(Func<MetricDto, Task> handler, CancellationToken token);
}
