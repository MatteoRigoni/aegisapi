using Gateway.Resilience;
using Gateway.Settings;

var builder = WebApplication.CreateBuilder(args);

// Settings
builder.Services.Configure<ResilienceSettings>(
    builder.Configuration.GetSection("Resilience"));

// Resilience v8 per YARP: factory custom che avvolge l'handler
builder.Services.AddSingleton<Yarp.ReverseProxy.Forwarder.IForwarderHttpClientFactory,
                              ResilienceForwarderHttpClientFactory>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapGet("/", () => "AegisAPI Gateway up");
app.MapGet("/healthz", () => Results.Ok());

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

app.MapReverseProxy();
app.Run();

public partial class Program { }
