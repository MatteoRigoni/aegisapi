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
builder.Services.AddServerSideBlazor().AddCircuitOptions(o => { o.DetailedErrors = true; }); 
builder.Services.AddSignalR();
builder.Services.AddScoped<Dashboard.Services.MudThemeManager>();

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("GatewayUrls"));
var useMocks = builder.Configuration.GetValue<bool>("UseMocks");

if (useMocks)
{
    builder.Services.AddScoped<IMetricsService, MockMetricsService>();
    builder.Services.AddScoped<IIncidentService, MockIncidentService>();
    builder.Services.AddScoped<IControlPlaneService, MockControlPlaneService>();
    builder.Services.AddScoped<ISummarizerService, MockSummarizerService>();
    builder.Services.AddScoped<INetworkService, MockNetworkService>();
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
    builder.Services.AddHttpClient<RealSummarizerService>((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
        client.BaseAddress = new Uri(opts.Summarizer);
    });

    builder.Services.AddHttpClient<RealNetworkService>((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
        client.BaseAddress = new Uri(opts.Network);
    });

    builder.Services.AddScoped<IMetricsService, RealMetricsService>();
    builder.Services.AddScoped<IIncidentService, RealIncidentService>();
    builder.Services.AddScoped<IControlPlaneService, RealControlPlaneService>();
    builder.Services.AddScoped<ISummarizerService, RealSummarizerService>();
    builder.Services.AddScoped<INetworkService, RealNetworkService>();
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
