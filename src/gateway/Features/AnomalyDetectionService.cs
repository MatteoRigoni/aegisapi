using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Gateway.Features;

public class AnomalyDetectionService : BackgroundService
{
    private readonly IFeatureSource _queue;
    private readonly IAnomalyDetector _detector;
    private readonly ILogger<AnomalyDetectionService> _logger;
    private static readonly Meter _meter = new("aegis");
    private static readonly Counter<long> _counter = _meter.CreateCounter<long>("aegis.anomalies");

    public ConcurrentBag<(RequestFeature feature, string reason)> Anomalies { get; } = new();

    public AnomalyDetectionService(IFeatureSource queue, IAnomalyDetector detector, ILogger<AnomalyDetectionService> logger)
    {
        _queue = queue;
        _detector = detector;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var feature in _queue.DequeueAllAsync(stoppingToken))
        {
            if (_detector.Observe(feature, out var reason))
            {
                Anomalies.Add((feature, reason));
                var detectorTag = reason.StartsWith("ml_") ? "ml" : "rules";
                _counter.Add(1, new("reason", reason), new("detector", detectorTag));
                _logger.LogInformation("Anomaly {Reason} for {Route}", reason, feature.RouteKey);
            }
        }
    }
}
