using System.Collections.Concurrent;
using Gateway.ControlPlane.Models;

namespace Gateway.ControlPlane.Stores;

public interface IRateLimitPlanStore
{
    IEnumerable<RateLimitPlan> GetAll();
    (RateLimitPlan plan, string etag) Add(RateLimitPlan plan);
    bool TryUpdate(string plan, RateLimitPlan updated, string? etag, out string newEtag, out RateLimitPlan? before);
    bool TryRemove(string plan, string? etag, out RateLimitPlan? before);
    (RateLimitPlan plan, string etag)? Get(string plan);
}

public sealed class InMemoryRateLimitStore : IRateLimitPlanStore
{
    private sealed record Entry(RateLimitPlan Plan, string ETag);
    private readonly ConcurrentDictionary<string, Entry> _plans = new();

    public IEnumerable<RateLimitPlan> GetAll() => _plans.Values.Select(e => e.Plan);

    public (RateLimitPlan plan, string etag)? Get(string plan)
        => _plans.TryGetValue(plan, out var e) ? (e.Plan, e.ETag) : null;

    public (RateLimitPlan plan, string etag) Add(RateLimitPlan plan)
    {
        var etag = Guid.NewGuid().ToString();
        _plans[plan.Plan] = new Entry(plan, etag);
        return (plan, etag);
    }

    public bool TryUpdate(string plan, RateLimitPlan updated, string? etag, out string newEtag, out RateLimitPlan? before)
    {
        newEtag = Guid.NewGuid().ToString();
        before = null;
        if (!_plans.TryGetValue(plan, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Plan;
        return _plans.TryUpdate(plan, new Entry(updated, newEtag), existing);
    }

    public bool TryRemove(string plan, string? etag, out RateLimitPlan? before)
    {
        before = null;
        if (!_plans.TryGetValue(plan, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Plan;
        return _plans.TryRemove(plan, out _);
    }
}
