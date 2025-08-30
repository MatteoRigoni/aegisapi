using Dashboard.Models;

namespace Dashboard.Services;

public interface IControlPlaneService
{
    Task ApplyFixAsync(string incidentId);
    Task<IReadOnlyList<ApiKeyDto>> GetApiKeysAsync();
    Task<ApiKeyDto> CreateApiKeyAsync(ApiKeyDto key);
    Task DeleteApiKeyAsync(string id);

    Task<IReadOnlyList<RouteDto>> GetRoutesAsync();
    Task<RouteDto> CreateRouteAsync(RouteDto route);
    Task DeleteRouteAsync(string id);

    Task<IReadOnlyList<RateLimitPlanDto>> GetRateLimitPlansAsync();
    Task<RateLimitPlanDto> CreateRateLimitPlanAsync(RateLimitPlanDto plan);
    Task DeleteRateLimitPlanAsync(string plan);

    Task<IReadOnlyList<WafRuleDto>> GetWafRulesAsync();
    Task ToggleWafRuleAsync(string rule, bool enabled);

    Task<IReadOnlyList<AuditEntryDto>> GetAuditEntriesAsync();
}
