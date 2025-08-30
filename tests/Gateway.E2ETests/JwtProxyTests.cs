using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.E2ETests;

public class JwtProxyTests
{
    [Fact]
    public async Task Secure_Endpoint_With_Valid_Token_Proxies_To_Backend()
    {
        // Start fake backend on random port with counter
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var calls = 0;
        var backend = builder.Build();
        backend.MapGet("/ping", () => { calls++; return "pong-secure"; });
        await backend.StartAsync();
        var backendUrl = backend.Urls.First().TrimEnd('/') + "/";

        var jwtKey = "dev-secret-please-change-this-key-32bytes-min";

        // Configure gateway to use backend and auth key
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ReverseProxy:Clusters:backend:Destinations:d1:Address"] = backendUrl,
                        ["Auth:JwtKey"] = jwtKey
                    });
                });
            });

        var client = factory.CreateClient();

        // Create token with scope
        var token = CreateToken(jwtKey, new[] { new Claim("scope", "api.read") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/secure/ping");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("pong-secure", await resp.Content.ReadAsStringAsync());
        Assert.Equal(1, calls);

        await backend.StopAsync();
        await backend.DisposeAsync();
    }

    private static string CreateToken(string key, IEnumerable<Claim> claims, DateTime? expires = null)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var exp = expires ?? DateTime.UtcNow.AddMinutes(5);
        var nbf = exp.AddHours(-1);
        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: nbf,
            expires: exp,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

