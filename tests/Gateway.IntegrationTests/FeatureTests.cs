using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Gateway.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.IntegrationTests;

public class FeatureTests
{
    private static async Task WaitForFeaturesAsync(FeatureConsumer consumer, int count)
    {
        var sw = Stopwatch.StartNew();
        while (consumer.Features.Count < count && sw.ElapsedMilliseconds < 1000)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task ProducesFeatureForSuccessfulRequest()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("test-agent");

        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();

        var consumer = factory.Services.GetRequiredService<FeatureConsumer>();
        await WaitForFeaturesAsync(consumer, 1);

        var feature = Assert.Single(consumer.Features);
        Assert.Equal("/", feature.Path);
        Assert.Equal(200, feature.Status);
        Assert.False(feature.SchemaError);
    }

    [Fact]
    public async Task ProducesFeatureForSchemaError()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("test-agent");

        var response = await client.PostAsJsonAsync("/api/echo", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var consumer = factory.Services.GetRequiredService<FeatureConsumer>();
        await WaitForFeaturesAsync(consumer, 1);

        var feature = Assert.Single(consumer.Features);
        Assert.Equal("/api/echo", feature.Path);
        Assert.Equal(400, feature.Status);
        Assert.True(feature.SchemaError);
    }

    [Fact]
    public async Task CanSeedFakeData()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var queue = factory.Services.GetRequiredService<IRequestFeatureQueue>();
        queue.Seed(new[] { new RequestFeature("seed-client", 1, 0.5, "/seed", 200, false) });

        var consumer = factory.Services.GetRequiredService<FeatureConsumer>();
        await WaitForFeaturesAsync(consumer, 1);

        var feature = Assert.Single(consumer.Features);
        Assert.Equal("seed-client", feature.ClientId);
    }
}
