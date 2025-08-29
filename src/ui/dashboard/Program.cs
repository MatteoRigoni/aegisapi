using Dashboard.Services;
using Dashboard.Hubs;

var builder = WebApplication.CreateBuilder(args);

var useMocks = builder.Configuration.GetValue("UseMocks", true);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

if (useMocks)
{
    builder.Services.AddSingleton<IIncidentsService, MockIncidentsService>();
    builder.Services.AddSingleton<IControlPlaneService, MockControlPlaneService>();
    builder.Services.AddSingleton<IPolicyService, MockPolicyService>();
    builder.Services.AddHostedService<MetricsBroadcaster>();
}
else
{
    builder.Services.AddSingleton<IIncidentsService, ApiIncidentsService>();
    builder.Services.AddSingleton<IControlPlaneService, ApiControlPlaneService>();
    builder.Services.AddSingleton<IPolicyService, ApiPolicyService>();
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
