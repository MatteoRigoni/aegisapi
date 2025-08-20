using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Gateway.Features;

public class AnomalyDetectionService : BackgroundService
{
    private readonly IFeatureSource _queue;
    private readonly IAnomalyDetector _detector;

    public ConcurrentBag<(RequestFeature feature, string reason)> Anomalies { get; } = new();

    public AnomalyDetectionService(IFeatureSource queue, IAnomalyDetector detector)
    {
        _queue = queue;
        _detector = detector;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var feature in _queue.DequeueAllAsync(stoppingToken))
        {
            if (_detector.Observe(feature, out var reason))
                Anomalies.Add((feature, reason));
        }
    }
}
