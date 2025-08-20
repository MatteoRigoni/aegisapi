using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Collections.Generic;
using Gateway.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.IntegrationTests;

public class FeatureTests
{
    private static async Task WaitForAnomaliesAsync(AnomalyDetectionService service, int count)
    {
        var sw = Stopwatch.StartNew();
        while (service.Anomalies.Count < count && sw.ElapsedMilliseconds < 1000)
        {
            await Task.Delay(10);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AnomalyDetection:RpsThreshold"] = "0",
                    ["AnomalyDetection:FourXxThreshold"] = "0",
                    ["AnomalyDetection:FiveXxThreshold"] = "0",
                    ["AnomalyDetection:WafThreshold"] = "0",
                    ["AnomalyDetection:UaEntropyThreshold"] = "0",
                });
            });
        });

    [Fact]
    public async Task ProducesFeatureForSuccessfulRequest()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("test-agent");

        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();

        var service = factory.Services.GetRequiredService<AnomalyDetectionService>();
        await WaitForAnomaliesAsync(service, 1);

        var (feature, _) = Assert.Single(service.Anomalies);
        Assert.Equal("/", feature.Path);
        Assert.Equal(200, feature.Status);
        Assert.False(feature.SchemaError);
    }

    [Fact]
    public async Task ProducesFeatureForSchemaError()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("test-agent");

        var response = await client.PostAsJsonAsync("/api/echo", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var service = factory.Services.GetRequiredService<AnomalyDetectionService>();
        await WaitForAnomaliesAsync(service, 1);

        var (feature, _) = Assert.Single(service.Anomalies);
        Assert.Equal("/api/echo", feature.Path);
        Assert.Equal(400, feature.Status);
        Assert.True(feature.SchemaError);
    }

    [Fact]
    public async Task CanSeedFakeData()
    {
        await using var factory = CreateFactory();
        var queue = factory.Services.GetRequiredService<IRequestFeatureQueue>();
        queue.Seed(new[] { new RequestFeature("seed-client", 1, 0.5, "/seed", 200, false) });

        var service = factory.Services.GetRequiredService<AnomalyDetectionService>();
        await WaitForAnomaliesAsync(service, 1);

        var (feature, _) = Assert.Single(service.Anomalies);
        Assert.Equal("seed-client", feature.ClientId);
    }
}
