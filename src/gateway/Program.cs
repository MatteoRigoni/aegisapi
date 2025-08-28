using Gateway;
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
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Usa il metodo di estensione per configurare servizi e logging
builder.Services.AddGatewayServices(builder.Configuration, builder.Logging);

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
