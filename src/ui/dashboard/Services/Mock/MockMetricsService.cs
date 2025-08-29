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

        connection.On<MetricDto>("metrics", async m => await handler(new MetricDto(
            m.Cpu,
            m.Memory,
            new Random().Next(50, 500), // Simulated Active Users
            new Random().NextDouble() * 5 // Simulated Error Rate
        )));

        await connection.StartAsync(token);
    }
}
