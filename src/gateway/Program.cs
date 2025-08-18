using Gateway.Features;
using Gateway.Observability;
using Gateway.RateLimiting;
using Gateway.Resilience;
using Gateway.Security;
using Gateway.Settings;
using Gateway.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Resilience configuration
builder.Services.Configure<ResilienceSettings>(builder.Configuration.GetSection("Resilience"));
builder.Services.Configure<RateLimitingSettings>(builder.Configuration.GetSection("RateLimiting"));
builder.Services.Configure<WafSettings>(builder.Configuration.GetSection("Waf"));
builder.Services.AddMemoryCache();

const string ApiKeyScheme = "ApiKey";
const string BearerOrApiKeyScheme = "BearerOrApiKey";

// Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = BearerOrApiKeyScheme;
    options.DefaultChallengeScheme = BearerOrApiKeyScheme;
})
.AddPolicyScheme(BearerOrApiKeyScheme, JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.ForwardDefaultSelector = ctx =>
    {
        if (ctx.Request.Headers.ContainsKey("Authorization"))
            return JwtBearerDefaults.AuthenticationScheme;
        if (ctx.Request.Headers.ContainsKey("X-API-Key"))
            return ApiKeyScheme;
        return JwtBearerDefaults.AuthenticationScheme;
    };
})
.AddJwtBearer(options =>
{
    var jwtKey = builder.Configuration["Auth:JwtKey"] ?? "dev-secret";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyScheme, options =>
{
    options.ClaimsIssuer = ApiKeyScheme;
});

// Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiReadOrKey", policy =>
    {
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("scope", "api.read") ||
            ctx.User.HasClaim("ApiKey", "Valid"));
    });
});

// ApiKey configuration
builder.Services
    .AddOptions<ApiKeyValidationOptions>()
    .Configure<IConfiguration>((opt, cfg) => opt.Hash = cfg["Auth:ApiKeyHash"] ?? "");

// YARP resilience
builder.Services.AddSingleton<Yarp.ReverseProxy.Forwarder.IForwarderHttpClientFactory, ResilienceForwarderHttpClientFactory>();
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddSingleton<IRequestFeatureQueue, RequestFeatureQueue>();
builder.Services.AddSingleton<FeatureConsumer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FeatureConsumer>());

builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb.AddService("gateway"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        metrics.AddRuntimeInstrumentation();
        metrics.AddProcessInstrumentation();
        metrics.AddMeter(GatewayDiagnostics.MeterName);
        metrics.AddPrometheusExporter();
        metrics.AddOtlpExporter();
    });

builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeScopes = true;
    options.IncludeFormattedMessage = true;
    options.ParseStateValues = true;
    options.AddOtlpExporter();
});

var app = builder.Build();

app.UseMiddleware<FeatureCollectorMiddleware>();

// Security headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'";
    await next();
});

app.UseMiddleware<WafMiddleware>();
app.UseAuthentication();
app.UseMiddleware<ClientRateLimiterMiddleware>();
app.UseAuthorization();
app.UseMiddleware<JsonValidationMiddleware>();

app.MapGet("/", () => "AegisAPI Gateway up");
app.MapGet("/healthz", () => Results.Ok());
app.MapPost("/api/echo", (System.Text.Json.JsonElement payload) => Results.Json(payload));
app.MapReverseProxy();
app.MapPrometheusScrapingEndpoint();

app.Run();

public partial class Program { }
