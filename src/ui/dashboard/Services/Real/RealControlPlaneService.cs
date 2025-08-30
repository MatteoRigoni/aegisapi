using System.Net.Http.Json;
using Dashboard.Models;

namespace Dashboard.Services.Real;

public class RealControlPlaneService : IControlPlaneService
{
    private readonly HttpClient _http;
    public RealControlPlaneService(HttpClient http) => _http = http;

    public async Task ApplyFixAsync(string incidentId)
    {
        var res = await _http.PostAsync($"cp/incidents/{incidentId}/apply", null);
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ApiKeyDto>> GetApiKeysAsync()
        => await _http.GetFromJsonAsync<List<ApiKeyDto>>("cp/apikeys") ?? new();

    public async Task<ApiKeyDto> CreateApiKeyAsync(ApiKeyDto key)
    {
        var res = await _http.PostAsJsonAsync("cp/apikeys", key);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<ApiKeyDto>() ?? key;
    }

    public async Task DeleteApiKeyAsync(string id)
    {
        var res = await _http.DeleteAsync($"cp/apikeys/{id}");
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<RouteDto>> GetRoutesAsync()
        => await _http.GetFromJsonAsync<List<RouteDto>>("cp/routes") ?? new();

    public async Task<RouteDto> CreateRouteAsync(RouteDto route)
    {
        var res = await _http.PostAsJsonAsync("cp/routes", route);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<RouteDto>() ?? route;
    }

    public async Task DeleteRouteAsync(string id)
    {
        var res = await _http.DeleteAsync($"cp/routes/{id}");
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<RateLimitPlanDto>> GetRateLimitPlansAsync()
        => await _http.GetFromJsonAsync<List<RateLimitPlanDto>>("cp/ratelimits") ?? new();

    public async Task<RateLimitPlanDto> CreateRateLimitPlanAsync(RateLimitPlanDto plan)
    {
        var res = await _http.PostAsJsonAsync("cp/ratelimits", plan);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<RateLimitPlanDto>() ?? plan;
    }

    public async Task DeleteRateLimitPlanAsync(string plan)
    {
        var res = await _http.DeleteAsync($"cp/ratelimits/{plan}");
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<WafRuleDto>> GetWafRulesAsync()
        => await _http.GetFromJsonAsync<List<WafRuleDto>>("cp/waf") ?? new();

    public async Task ToggleWafRuleAsync(string rule, bool enabled)
    {
        var res = await _http.PostAsJsonAsync($"cp/waf/{rule}", new { enabled });
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditEntriesAsync()
        => await _http.GetFromJsonAsync<List<AuditEntryDto>>("cp/audit") ?? new();
}
