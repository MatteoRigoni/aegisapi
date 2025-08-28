using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Summarizer.Model;
using Summarizer.Llm;
using Summarizer.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILlmClient, FakeLlmClient>();
builder.Services.AddSingleton<SummarizerService>();

builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, SimpleApiKeyHandler>("InternalKey", null);
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/ai/summarize", async ([FromBody] IncidentBundle bundle, SummarizerService svc) =>
{
    var result = await svc.SummarizeAsync(bundle);
    return Results.Json(result);
}).RequireAuthorization();

app.MapGet("/seed/logs", () => SeedData.GetSampleLogBundle());

app.Run();
public partial class Program { }
