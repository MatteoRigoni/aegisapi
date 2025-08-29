using Dashboard.Hubs;
using Dashboard.Options;
using Dashboard.Services;
using Dashboard.Services.Mock;
using Dashboard.Services.Real;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMudServices();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("GatewayUrls"));
var useMocks = builder.Configuration.GetValue<bool>("UseMocks");

if (useMocks)
{
    builder.Services.AddSingleton<IMetricsService, MockMetricsService>();
    builder.Services.AddSingleton<IIncidentService, MockIncidentService>();
    builder.Services.AddSingleton<IControlPlaneService, MockControlPlaneService>();
}
else
{
    builder.Services.AddHttpClient<RealMetricsService>((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
        client.BaseAddress = new Uri(opts.Metrics);
    });
    builder.Services.AddHttpClient<RealIncidentService>((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
        client.BaseAddress = new Uri(opts.Incidents);
    });
    builder.Services.AddHttpClient<RealControlPlaneService>((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
        client.BaseAddress = new Uri(opts.Control);
    });
    builder.Services.AddScoped<IMetricsService, RealMetricsService>();
    builder.Services.AddScoped<IIncidentService, RealIncidentService>();
    builder.Services.AddScoped<IControlPlaneService, RealControlPlaneService>();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapHub<MetricsHub>("/hubs/metrics");
app.MapFallbackToPage("/_Host");

app.Run();
