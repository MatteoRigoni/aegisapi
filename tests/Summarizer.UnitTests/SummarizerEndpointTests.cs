using Microsoft.AspNetCore.Mvc.Testing;
using Summarizer.Model;
using System.Net.Http.Json;

namespace Summarizer.UnitTests;

public class SummarizerEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SummarizerEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task PostSummarize_ReturnsSummary()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Key", "dev");
        var bundle = SeedData.GetSampleLogBundle();
        var res = await client.PostAsJsonAsync("/ai/summarize", bundle);
        res.EnsureSuccessStatusCode();
        var summary = await res.Content.ReadFromJsonAsync<SummaryResponse>();
        Assert.NotNull(summary);
        Assert.False(string.IsNullOrEmpty(summary!.Summary));
    }
}
