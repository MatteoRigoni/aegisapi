using Gateway.Resilience;
using Gateway.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Gateway.Security;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Settings
builder.Services.Configure<ResilienceSettings>(
    builder.Configuration.GetSection("Resilience"));

// Authentication & Authorization
const string ApiKeyScheme = "ApiKey";
var jwtKey = builder.Configuration["Auth:JwtKey"] ?? "dev-secret";
var apiKeyHash = builder.Configuration["Auth:ApiKeyHash"] ?? string.Empty;

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "BearerOrApiKey";
    options.DefaultChallengeScheme = "BearerOrApiKey";
})
.AddPolicyScheme("BearerOrApiKey", JwtBearerDefaults.AuthenticationScheme, options =>
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiReadOrKey", policy =>
    {
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("scope", "api.read") ||
            ctx.User.HasClaim("ApiKey", "Valid"));
    });
});

builder.Services.AddSingleton(new ApiKeyValidationOptions(apiKeyHash));

// Resilience v8 per YARP: factory custom che avvolge l'handler
builder.Services.AddSingleton<Yarp.ReverseProxy.Forwarder.IForwarderHttpClientFactory,
                              ResilienceForwarderHttpClientFactory>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// security headers (tuoi)
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "AegisAPI Gateway up");
app.MapGet("/healthz", () => Results.Ok());

app.MapReverseProxy();
app.Run();

public partial class Program { }
