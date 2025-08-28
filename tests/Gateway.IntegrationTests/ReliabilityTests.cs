// tests/Gateway.IntegrationTests/ReliabilityTests.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace Gateway.IntegrationTests;

public class ReliabilityTests
{
    // Shared helpers

    private static async Task<BackendHost> StartBackendAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        map(app); // Map endpoints before starting
        await app.StartAsync();

        return new BackendHost(app);
    }

    private static WebApplicationFactory<Program> CreateGatewayFactory(
        string backendUrl,
        IDictionary<string, string?>? extra = null)
    {
        if (!backendUrl.EndsWith("/")) backendUrl += "/";

        var baseConfig = new Dictionary<string, string?>
        {
            // YARP routing & transforms
            ["ReverseProxy:Routes:api:ClusterId"] = "backend",
            ["ReverseProxy:Routes:api:Match:Path"] = "/api/{**catchAll}",
            ["ReverseProxy:Routes:api:Transforms:0:PathRemovePrefix"] = "/api",
            // Ephemeral backend
            ["ReverseProxy:Clusters:backend:Destinations:d1:Address"] = backendUrl,
            // Default: prevent YARP from interfering with retry/breaker tests (override for timeout test)
            ["ReverseProxy:Clusters:backend:HttpRequest:ActivityTimeout"] = "00:00:10",

            // Reasonable resilience defaults (Polly v8)
            ["Resilience:Retry:Count"] = "1",                 // Minimum allowed
            ["Resilience:Retry:BaseDelayMs"] = "100",
            ["Resilience:Timeout:DurationSeconds"] = "2",
            ["Resilience:CircuitBreaker:BreakDurationSeconds"] = "5"
        };

        if (extra is not null)
        {
            foreach (var kv in extra)
                baseConfig[kv.Key] = kv.Value;
        }

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Testing");
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.Sources.Clear();
                    cfg.AddInMemoryCollection(baseConfig);
                });
            });
    }

    private sealed class BackendHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string Url { get; }

        public BackendHost(WebApplication app)
        {
            _app = app;
            Url = _app.Urls.Single();
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    // Tests

    [Fact]
    public async Task Should_Timeout_On_Slow_Backend()
    {
        // Backend responds slowly (>2s)
        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/slow", async ctx =>
            {
                await Task.Delay(3000);
                await ctx.Response.WriteAsync("too late");
            });
        });

        // Force YARP timeout to 2s and keep Polly timeout higher,
        // so the proxy always returns 504 (even if Retry.Count >= 1)
        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:backend:HttpRequest:ActivityTimeout"] = "00:00:02",
                ["Resilience:Timeout:DurationSeconds"] = "5",   // > 2s to avoid Polly timeout first
                ["Resilience:Retry:Count"] = "1"                // Minimum allowed
            });

        var client = factory.CreateClient();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await client.GetAsync("/api/slow");
        sw.Stop();

        Assert.Equal(HttpStatusCode.GatewayTimeout, resp.StatusCode);
        Assert.InRange(sw.Elapsed, TimeSpan.FromSeconds(1.8), TimeSpan.FromSeconds(3.0));
    }

    [Fact]
    public async Task Should_Retry_On_Flaky_Backend()
    {
        var attempts = 0;

        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/flaky", async ctx =>
            {
                attempts++;
                if (attempts <= 2)
                {
                    ctx.Response.StatusCode = 503;
                    await ctx.Response.WriteAsync("Service Unavailable");
                    return;
                }
                await ctx.Response.WriteAsync("success after retry");
            });
        });

        // Increase ActivityTimeout to avoid triggering before Polly retries
        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:backend:HttpRequest:ActivityTimeout"] = "00:00:10",
                ["Resilience:Timeout:DurationSeconds"] = "2",
                ["Resilience:Retry:Count"] = "2"   // Two retries => 3 total attempts
            });

        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/flaky");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("success after retry", await resp.Content.ReadAsStringAsync());
        Assert.Equal(3, attempts); // 1 initial + 2 retries
    }

    [Fact]
    public async Task Should_Open_CircuitBreaker_After_Consecutive_Failures()
    {
        var requestCount = 0;

        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/error", ctx =>
            {
                requestCount++;
                ctx.Response.StatusCode = 500;
                return Task.CompletedTask;
            });
        });

        // Avoid ActivityTimeout interference; keep it high.
        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:backend:HttpRequest:ActivityTimeout"] = "00:00:10",
                ["Resilience:Timeout:DurationSeconds"] = "2",
                ["Resilience:Retry:Count"] = "1" // Minimum: final outcome remains failure
            });

        var client = factory.CreateClient();

        // Generate enough failures to satisfy MinimumThroughput (=10 in custom factory)
        for (int i = 0; i < 12; i++)
        {
            await client.GetAsync("/api/error");
            await Task.Delay(50);
        }

        Assert.True(requestCount >= 10); // Breaker has enough samples

        // Circuit should now be OPEN: next call should be short-circuited
        var before = requestCount;
        var response = await client.GetAsync("/api/error");
        var after = requestCount;

        // 1) Short-circuit should not hit backend
        Assert.Equal(before, after);

        // 2) Accept 502 or 503 as open circuit result
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable });

        // Wait for circuit to close
        await Task.Delay(TimeSpan.FromSeconds(5.5));

        // Half-open/closed: request should hit backend and produce 500
        var beforeClose = requestCount;
        var final = await client.GetAsync("/api/error");
        var afterClose = requestCount;

        Assert.Equal(HttpStatusCode.InternalServerError, final.StatusCode);
        Assert.Equal(beforeClose + 2, afterClose); // Backend hit again with Retry = 1
    }
}
