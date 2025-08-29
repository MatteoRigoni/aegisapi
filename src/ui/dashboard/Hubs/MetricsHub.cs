using Dashboard.Models;
using Microsoft.AspNetCore.SignalR;

namespace Dashboard.Hubs;

public class MetricsHub : Hub
{
    private static readonly Random rnd = new();

    public async Task SendMetrics()
    {
        while (true)
        {
            var metric = new MetricDto(
                rnd.NextDouble() * 100, // CPU Usage
                rnd.Next(0, 32000),    // Memory Usage
                rnd.Next(50, 500),     // Active Users
                rnd.NextDouble() * 5   // Error Rate
            );

            await Clients.All.SendAsync("metrics", metric);
            await Task.Delay(1000);
        }
    }
}
