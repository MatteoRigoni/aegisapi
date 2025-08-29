using Dashboard.Models;
using Microsoft.AspNetCore.SignalR;

namespace Dashboard.Hubs;

public class MetricsHub : Hub
{
    public override Task OnConnectedAsync()
    {
        _ = SendLoop(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    private async Task SendLoop(string connectionId)
    {
        var rnd = new Random();
        while (true)
        {
            var metric = new MetricDto(rnd.NextDouble() * 100, rnd.Next(0, 32000), rnd.Next(0, 1000));
            await Clients.Client(connectionId).SendAsync("metrics", metric);
            await Task.Delay(1000);
        }
    }
}
