using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Gateway.IntegrationTests;

public class SecurityTests
{
    private static string CreateToken(string key, IEnumerable<Claim> claims, DateTime? expires = null)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(claims: claims, expires: expires ?? DateTime.UtcNow.AddMinutes(5), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static WebApplicationFactory<Program> CreateFactory(string jwtKey, string apiKeyHash)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:JwtKey"] = jwtKey,
                    ["Auth:ApiKeyHash"] = apiKeyHash
                });
            });
        });
    }

    [Fact]
    public async Task Secure_With_Valid_Token_Returns_200()
    {
        const string jwtKey = "test-secret";
        var token = CreateToken(jwtKey, new[] { new Claim("scope", "api.read") });

        using var factory = CreateFactory(jwtKey, Hash("apikey"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await client.GetAsync("/api/secure");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Secure_With_Missing_Scope_Returns_403()
    {
        const string jwtKey = "test-secret";
        var token = CreateToken(jwtKey, Array.Empty<Claim>());

        using var factory = CreateFactory(jwtKey, Hash("apikey"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await client.GetAsync("/api/secure");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Secure_With_Expired_Token_Returns_401()
    {
        const string jwtKey = "test-secret";
        var token = CreateToken(jwtKey, new[] { new Claim("scope", "api.read") }, DateTime.UtcNow.AddMinutes(-5));

        using var factory = CreateFactory(jwtKey, Hash("apikey"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await client.GetAsync("/api/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Secure_With_Valid_ApiKey_Returns_200()
    {
        var apiKey = "goodkey";
        using var factory = CreateFactory("test-secret", Hash(apiKey));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

        var resp = await client.GetAsync("/api/secure");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Secure_With_Wrong_ApiKey_Returns_401()
    {
        using var factory = CreateFactory("test-secret", Hash("expected"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "wrong");

        var resp = await client.GetAsync("/api/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Secure_With_No_Credentials_Returns_401()
    {
        using var factory = CreateFactory("test-secret", Hash("expected"));
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
