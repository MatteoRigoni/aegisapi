using Dashboard.Models;
using Microsoft.AspNetCore.SignalR;

namespace Dashboard.Hubs;

public class MetricsHub : Hub
{
    private static readonly Random rnd = new();

    public async Task SendMetrics()
    {
        var token = Context.ConnectionAborted;
        while (!token.IsCancellationRequested)
        {
            var metric = new MetricDto(
                rnd.NextDouble() * 100, // Requests per second
                rnd.NextDouble() * 8,   // UA entropy
                rnd.Next(0, 20),        // Schema errors
                rnd.Next(0, 10)         // WAF blocks
            );

            await Clients.Caller.SendAsync("metrics", metric, token);
            await Task.Delay(1000, token);
        }
    }
}
