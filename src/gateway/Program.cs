using Gateway;
using Gateway.Features;
using Gateway.Observability;
using Gateway.RateLimiting;
using Gateway.Resilience;
using Gateway.Security;
using Gateway.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGatewayServices(builder.Configuration, builder.Logging);

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

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => "AegisAPI Gateway up");
app.MapGet("/healthz", () => Results.Ok());
app.MapPost("/api/echo", async (HttpContext ctx) =>
{
    // mark as WAF hit when ?waf=1 is present
    if (ctx.Request.Query.ContainsKey("waf"))
        ctx.Items["WafHit"] = true;

    // allow forcing a status code via ?status=XXX
    if (ctx.Request.Query.TryGetValue("status", out var s) &&
        int.TryParse(s, out var code))
        return Results.StatusCode(code);

    var payload = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    return Results.Json(payload);
});
app.MapReverseProxy();
var meterProvider = app.Services.GetService<MeterProvider>();
if (meterProvider != null)
    app.MapPrometheusScrapingEndpoint(meterProvider);
app.MapControllers();

app.Run();

public partial class Program { }
