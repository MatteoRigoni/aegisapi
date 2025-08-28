using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace Gateway.IntegrationTests;

public class PingProxyTests
{
    [Fact]
    public async Task Root_And_Healthz_Work()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        Assert.Equal("AegisAPI Gateway up", await client.GetStringAsync("/"));
        var resp = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Api_Ping_Is_Forwarded_To_Backend()
    {
        // Ephemeral backend
        var backendBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        backendBuilder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var backendApp = backendBuilder.Build();
        backendApp.MapGet("/ping", () => "pong");
        await backendApp.StartAsync();

        var backendUrl = backendApp.Urls.Single(); // e.g. http://127.0.0.1:51023/

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Testing");
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    // Override YARP configuration
                    cfg.Sources.Clear();
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ReverseProxy:Clusters:backend:Destinations:d1:Address"] = backendUrl.EndsWith("/") ? backendUrl : backendUrl + "/",
                        // Explicit configuration
                        ["ReverseProxy:Routes:api:ClusterId"] = "backend",
                        ["ReverseProxy:Routes:api:Match:Path"] = "/api/{**catchAll}",
                        ["ReverseProxy:Routes:api:Transforms:0:PathRemovePrefix"] = "/api"
                    });
                });
            });

        var client = factory.CreateClient();

        var s = await client.GetStringAsync("/api/ping");
        Assert.Equal("pong", s);

        await backendApp.StopAsync();
    }
}
