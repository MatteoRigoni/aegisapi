using Dashboard.Models;
using System.Linq;

namespace Dashboard.Services.Mock;

public class MockControlPlaneService : IControlPlaneService
{
    private readonly List<ApiKeyDto> _apiKeys = new()
    {
        new ApiKeyDto("1", "Demo", "abc123")
    };

    private readonly List<RouteDto> _routes = new()
    {
        new RouteDto("api", "/api/{**catchall}", "https://backend/api")
    };

    private readonly List<RateLimitPlanDto> _plans = new()
    {
        new RateLimitPlanDto("free", 60)
    };

    private readonly List<WafRuleDto> _waf = new()
    {
        new WafRuleDto("SqlInjection", true)
    };

    private readonly List<AuditEntryDto> _audit = new()
    {
        new AuditEntryDto(DateTime.UtcNow, "created", "Demo")
    };

    public Task ApplyFixAsync(string incidentId) => Task.CompletedTask;

    public Task<IReadOnlyList<ApiKeyDto>> GetApiKeysAsync() => Task.FromResult((IReadOnlyList<ApiKeyDto>)_apiKeys);
    public Task<ApiKeyDto> CreateApiKeyAsync(ApiKeyDto key)
    {
        _apiKeys.Add(key);
        return Task.FromResult(key);
    }
    public Task DeleteApiKeyAsync(string id)
    {
        _apiKeys.RemoveAll(k => k.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RouteDto>> GetRoutesAsync() => Task.FromResult((IReadOnlyList<RouteDto>)_routes);
    public Task<RouteDto> CreateRouteAsync(RouteDto route)
    {
        _routes.Add(route);
        return Task.FromResult(route);
    }
    public Task DeleteRouteAsync(string id)
    {
        _routes.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RateLimitPlanDto>> GetRateLimitPlansAsync() => Task.FromResult((IReadOnlyList<RateLimitPlanDto>)_plans);
    public Task<RateLimitPlanDto> CreateRateLimitPlanAsync(RateLimitPlanDto plan)
    {
        _plans.Add(plan);
        return Task.FromResult(plan);
    }
    public Task DeleteRateLimitPlanAsync(string plan)
    {
        _plans.RemoveAll(p => p.Plan == plan);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WafRuleDto>> GetWafRulesAsync() => Task.FromResult((IReadOnlyList<WafRuleDto>)_waf);
    public Task ToggleWafRuleAsync(string rule, bool enabled)
    {
        var item = _waf.FirstOrDefault(w => w.Rule == rule);
        if (item != null)
        {
            _waf.Remove(item);
            _waf.Add(item with { Enabled = enabled });
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntryDto>> GetAuditEntriesAsync() => Task.FromResult((IReadOnlyList<AuditEntryDto>)_audit);
}
