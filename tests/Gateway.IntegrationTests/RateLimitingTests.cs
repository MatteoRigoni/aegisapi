using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Gateway.ControlPlane.Models;

namespace Gateway.IntegrationTests;

public class RateLimitingTests
{
    private const string JWT_KEY = "6d9f8fd111802be56c379d597842e29b2cebd35ff2133d431a49fa556a18703d";

    private static string CreateToken(string sub, string? plan = null)
    {
        var claims = new List<Claim> { new("sub", sub) };
        if (plan is not null)
            claims.Add(new("plan", plan));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JWT_KEY));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static WebApplicationFactory<Program> CreateFactory()
        => new GatewayFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("Auth:JwtKey", JWT_KEY);
                builder.UseSetting("Summarizer:BaseUrl", "http://localhost:5290");
                builder.UseSetting("RateLimiting:DefaultRpm", "2");
                builder.UseSetting("RateLimiting:Plans:gold", "4");
            });

    [Fact]
    public async Task Exceeding_Limit_Returns_429()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("user1"));

        var r1 = await client.GetAsync("/");
        var r2 = await client.GetAsync("/");
        var r3 = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
        Assert.True(r3.Headers.TryGetValues("Retry-After", out _));
    }

    [Fact]
    public async Task Separate_Clients_Do_Not_Throttle_Each_Other()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("clientA"));
        await client.GetAsync("/");
        await client.GetAsync("/");
        var blocked = await client.GetAsync("/");

        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("clientB"));
        var allowed = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Gold_Plan_Has_Higher_Limit()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("goldUser", "gold"));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var resp = await client.GetAsync("/");
            statuses.Add(resp.StatusCode);
        }

        Assert.True(statuses.Take(4).All(s => s == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses.Last());
    }

    [Fact]
    public async Task Updating_Plan_Takes_Effect_Without_Restart()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var plan = new RateLimitPlan { Plan = "silver", Rpm = 1 };
        var create = await client.PostAsJsonAsync("/cp/ratelimits", plan);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var etag = create.Headers.ETag!.Tag;

        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("userA", "silver"));
        var first = await client.GetAsync("/");
        var second = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        plan = plan with { Rpm = 3 };
        var putReq = new HttpRequestMessage(HttpMethod.Put, "/cp/ratelimits/silver")
        {
            Content = JsonContent.Create(plan)
        };
        putReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var update = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("userB", "silver"));
        var r1 = await client.GetAsync("/");
        var r2 = await client.GetAsync("/");
        var r3 = await client.GetAsync("/");
        var r4 = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r4.StatusCode);
    }
}
