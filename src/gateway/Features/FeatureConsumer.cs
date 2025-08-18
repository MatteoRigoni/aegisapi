using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;

namespace Gateway.Features;

public class FeatureConsumer : BackgroundService
{
    private readonly IRequestFeatureQueue _queue;
    public ConcurrentBag<RequestFeature> Features { get; } = new();

    public FeatureConsumer(IRequestFeatureQueue queue)
    {
        _queue = queue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var feature in _queue.DequeueAllAsync(stoppingToken))
        {
            Features.Add(feature);
        }
    }
}
