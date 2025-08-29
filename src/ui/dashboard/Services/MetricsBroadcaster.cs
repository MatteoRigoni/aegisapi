using Dashboard.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace Dashboard.Services;

public class MetricsBroadcaster : BackgroundService
{
    private readonly IHubContext<MetricsHub> _hub;
    private readonly Random _rand = new();
    private readonly string[] _names = ["CPU", "Memory", "Requests"];

    public MetricsBroadcaster(IHubContext<MetricsHub> hub) => _hub = hub;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var name in _names)
            {
                var metric = new Metric(name, _rand.NextDouble() * 100);
                await _hub.Clients.All.SendAsync("ReceiveMetric", metric, cancellationToken: stoppingToken);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
