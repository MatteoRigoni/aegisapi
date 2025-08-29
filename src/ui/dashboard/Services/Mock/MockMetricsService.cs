using Dashboard.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Dashboard.Services.Mock;

public class MockMetricsService : IMetricsService
{
    private readonly NavigationManager _nav;
    public MockMetricsService(NavigationManager nav) => _nav = nav;

    public async Task StartMetricsAsync(Func<MetricDto, Task> handler, CancellationToken token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(_nav.ToAbsoluteUri("/hubs/metrics"))
            .WithAutomaticReconnect()
            .Build();

 connection.On<MetricDto>("metrics", handler);

        await connection.StartAsync(token);
        _ = connection.SendAsync("SendMetrics", cancellationToken: token);
    }
}
