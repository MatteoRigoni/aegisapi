using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Linq;

namespace Gateway.E2ETests;

public class GatewayFactory : IAsyncLifetime
{
    private IHost? _backend;
    private WebApplicationFactory<Program>? _gateway;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Start minimal backend on random port
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var backendApp = builder.Build();
        backendApp.MapGet("/hello", () => "world");
        await backendApp.StartAsync();
        var backendUrl = backendApp.Urls.First().TrimEnd('/') + "/";
        _backend = backendApp;

        // Configure gateway to point to backend
        _gateway = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ReverseProxy:Clusters:backend:Destinations:d1:Address"] = backendUrl
                    });
                });
            });

        Client = _gateway.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        _gateway?.Dispose();

        if (_backend != null)
        {
            await _backend.StopAsync();
            _backend.Dispose();
        }
    }
}

