// tests/Gateway.IntegrationTests/SecurityTests.cs
using Gateway.ControlPlane.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Gateway.IntegrationTests;

public class SecurityTests
{
    private const string DEV_API_KEY = "1e1f8fd111802be56c379d597842e29b2cebd35ff2133d431a49fa556a18704e";

    // ===== Helpers =====

    private static async Task<BackendHost> StartBackendAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        map(app); // mappa gli endpoint PRIMA di start
        await app.StartAsync();

        return new BackendHost(app);
    }

    private static WebApplicationFactory<Program> CreateGatewayFactory(
        string backendUrl,
        IDictionary<string, string?>? extra = null)
    {
        if (!backendUrl.EndsWith("/")) backendUrl += "/";

        // Config base: YARP con route secure protetta da "ApiReadOrKey" e route public aperta.
        var baseConfig = new Dictionary<string, string?>
        {
            // ---- YARP routes ----
            // /api/secure/* -> richiede auth (AuthorizationPolicy = ApiReadOrKey)
            ["ReverseProxy:Routes:secure:Order"] = "0",
            ["ReverseProxy:Routes:secure:ClusterId"] = "backend",
            ["ReverseProxy:Routes:secure:Match:Path"] = "/api/secure/{**catchAll}",
            ["ReverseProxy:Routes:secure:AuthorizationPolicy"] = "ApiReadOrKey",
            ["ReverseProxy:Routes:secure:Transforms:0:PathRemovePrefix"] = "/api/secure",
            ["ReverseProxy:Routes:secure:Transforms:1:RequestHeadersCopy"] = "true",
            ["ReverseProxy:Routes:secure:Transforms:2:ResponseHeadersCopy"] = "true",

            // /api/* -> pubblico
            ["ReverseProxy:Routes:public:Order"] = "1",
            ["ReverseProxy:Routes:public:ClusterId"] = "backend",
            ["ReverseProxy:Routes:public:Match:Path"] = "/api/{**catchAll}",
            ["ReverseProxy:Routes:public:Transforms:0:PathRemovePrefix"] = "/api",
            ["ReverseProxy:Routes:public:Transforms:1:RequestHeadersCopy"] = "true",
            ["ReverseProxy:Routes:public:Transforms:2:ResponseHeadersCopy"] = "true",

            // ---- Cluster ----
            ["ReverseProxy:Clusters:backend:LoadBalancingPolicy"] = "PowerOfTwoChoices",
            ["ReverseProxy:Clusters:backend:Destinations:d1:Address"] = backendUrl,
            // Alziamo ActivityTimeout per evitare conflitti con test auth
            ["ReverseProxy:Clusters:backend:HttpRequest:ActivityTimeout"] = "00:00:10",

            // ---- Resilience defaults (se usati) ----
            ["Resilience:Timeout:DurationSeconds"] = "2",
            ["Resilience:Retry:Count"] = "2",
            ["Resilience:Retry:BaseDelayMs"] = "100",
            ["Resilience:CircuitBreaker:FailureThreshold"] = "5",
            ["Resilience:CircuitBreaker:BreakDurationSeconds"] = "5",

            // ---- Auth (verranno sovrascritti nei singoli test) ----
            ["Auth:JwtKey"] = "dev-secret",
            ["Auth:ApiKeyHash"] = Hash(DEV_API_KEY) // placeholder: ogni test può override
        };

        if (extra is not null)
        {
            foreach (var kv in extra)
                baseConfig[kv.Key] = kv.Value;
        }

        return new GatewayFactory()
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

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // ===== Tests =====

    [Fact]
    public async Task Public_Route_Does_Not_Require_Auth()
    {
        await using var backend = await StartBackendAsync(app =>
        {
            // Ricorda: /api/{**} ha PathRemovePrefix "/api" -> qui diventa "/ping"
            app.MapGet("/ping", () => Results.Text("pong", "text/plain"));
        });

        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["Auth:JwtKey"] = DEV_API_KEY,
                ["Auth:ApiKeyHash"] = Hash(DEV_API_KEY)
            });

        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/ping");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("pong", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Secure_With_Valid_Token_Returns_200()
    {
        await using var backend = await StartBackendAsync(app =>
        {
            // /api/secure/{**} ha PathRemovePrefix "/api/secure" -> qui "/ping"
            app.MapGet("/ping", () => Results.Text("pong-secure", "text/plain"));
        });

        var token = CreateToken(DEV_API_KEY, new[] { new Claim("scope", "api.read") });

        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["Auth:JwtKey"] = DEV_API_KEY,
                ["Auth:ApiKeyHash"] = Hash(DEV_API_KEY)
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await client.GetAsync("/api/secure/ping");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("pong-secure", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Secure_With_Missing_Scope_Returns_403()
    {
        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/ping", () => Results.Text("pong-secure", "text/plain"));
        });

        var token = CreateToken(DEV_API_KEY, Array.Empty<Claim>());

        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["Auth:JwtKey"] = DEV_API_KEY,
                ["Auth:ApiKeyHash"] = Hash("apikey") // non usato in questo test
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await client.GetAsync("/api/secure/ping");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Secure_With_Expired_Token_Returns_401()
    {
        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/ping", () => Results.Text("pong-secure", "text/plain"));
        });

        var token = CreateToken(DEV_API_KEY, new[] { new Claim("scope", "api.read") }, DateTime.UtcNow.AddMinutes(-10));

        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["Auth:JwtKey"] = DEV_API_KEY,
                ["Auth:ApiKeyHash"] = Hash("apikey") // non usato qui
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await client.GetAsync("/api/secure/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Secure_With_Valid_ApiKey_Returns_200()
    {
        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/ping", () => Results.Text("pong-secure", "text/plain"));
        });

        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["Auth:JwtKey"] = "dev-secret",            // irrilevante per API key
                ["Auth:ApiKeyHash"] = Hash(DEV_API_KEY)    // hash atteso lato server
            });

        var client = factory.CreateClient();
        // Lhandler calcola lhash: serve plaintext qui
        client.DefaultRequestHeaders.Add("X-API-Key", DEV_API_KEY);

        var resp = await client.GetAsync("/api/secure/ping");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("pong-secure", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Secure_With_Wrong_ApiKey_Returns_401()
    {
        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/ping", () => Results.Text("pong-secure", "text/plain"));
        });

        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["Auth:JwtKey"] = DEV_API_KEY,
                ["Auth:ApiKeyHash"] = Hash("expected") // atteso != inviato
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "wrong");

        var resp = await client.GetAsync("/api/secure/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Secure_With_No_Credentials_Returns_401()
    {
        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/ping", () => Results.Text("pong-secure", "text/plain"));
        });

        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["Auth:JwtKey"] = DEV_API_KEY,
                ["Auth:ApiKeyHash"] = Hash("expected")
            });

        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/secure/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ApiKey_Created_Used_Then_Deleted_Denies_Access()
    {
        const string newKey = "my-secret-key";

        await using var backend = await StartBackendAsync(app =>
        {
            app.MapGet("/ping", () => Results.Text("pong", "text/plain"));
        });

        using var factory = CreateGatewayFactory(
            backend.Url,
            new Dictionary<string, string?>
            {
                ["Auth:ApiKeyHash"] = ""
            });

        var client = factory.CreateClient();

        var route = new RouteConfig
        {
            Id = "secure",
            Path = "/secure/{**catchAll}",
            Destination = backend.Url,
            AuthorizationPolicy = "ApiReadOrKey",
            PathRemovePrefix = "/secure"
        };

        var createRoute = await client.PostAsJsonAsync("/cp/routes", route);
        Assert.Equal(HttpStatusCode.Created, createRoute.StatusCode);

        var record = new ApiKeyRecord { Id = "k1", Hash = Hash(newKey), Plan = "basic" };
        var createKey = await client.PostAsJsonAsync("/cp/apikeys", record);
        Assert.Equal(HttpStatusCode.Created, createKey.StatusCode);
        var etag = createKey.Headers.ETag!.Tag;

        client.DefaultRequestHeaders.Add("X-API-Key", newKey);
        var ok = await client.GetAsync("/secure/ping");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("pong", await ok.Content.ReadAsStringAsync());

        var delReq = new HttpRequestMessage(HttpMethod.Delete, "/cp/apikeys/k1");
        delReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var del = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var unauthorized = await client.GetAsync("/secure/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }
}
