using Dashboard.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Dashboard.Services.Mock;

public class MockMetricsService : IMetricsService
{
    private readonly NavigationManager _nav;
    private readonly ILogger<MockMetricsService> _logger;

    public MockMetricsService(NavigationManager nav, ILogger<MockMetricsService> logger)
    {
        _nav = nav;
        _logger = logger;
    }

    public async Task StartMetricsAsync(Func<MetricDto, Task> handler, CancellationToken token)
    {
        _logger.LogInformation("Initializing SignalR connection to /hubs/metrics");

        var connection = new HubConnectionBuilder()
            .WithUrl(_nav.ToAbsoluteUri("/hubs/metrics"))
            .WithAutomaticReconnect()
            .Build();

        connection.On<MetricDto>("metrics", async (metric) =>
        {
            _logger.LogInformation("Received metric: {Metric}", metric);
            await handler(metric);
        });

        try
        {
            await connection.StartAsync(token);
            _logger.LogInformation("SignalR connection established.");

            _ = connection.SendAsync("SendMetrics", cancellationToken: token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish SignalR connection.");
        }
    }
}
