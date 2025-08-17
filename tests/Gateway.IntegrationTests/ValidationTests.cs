using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gateway.IntegrationTests;

public class ValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ValidationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MissingRequiredField_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/echo", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var error = doc.RootElement.GetProperty("errors")[0];
        Assert.Contains("message", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WrongType_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/echo", new { message = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var error = doc.RootElement.GetProperty("errors")[0];
        Assert.Equal("/message", error.GetProperty("path").GetString());
    }

    [Fact]
    public async Task ExtraProperty_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/echo", new { message = "hi", extra = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var error = doc.RootElement.GetProperty("errors")[0];
        Assert.Equal("/extra", error.GetProperty("path").GetString());
    }
}
