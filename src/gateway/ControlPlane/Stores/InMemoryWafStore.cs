using System.Collections.Concurrent;
using Gateway.ControlPlane.Models;

namespace Gateway.ControlPlane.Stores;

public interface IWafToggleStore
{
    IEnumerable<WafToggle> GetAll();
    (WafToggle toggle, string etag) Add(WafToggle toggle);
    bool TryUpdate(string rule, WafToggle toggle, string? etag, out string newEtag, out WafToggle? before);
    bool TryRemove(string rule, string? etag, out WafToggle? before);
    (WafToggle toggle, string etag)? Get(string rule);
}

public sealed class InMemoryWafStore : IWafToggleStore
{
    private sealed record Entry(WafToggle Toggle, string ETag);
    private readonly ConcurrentDictionary<string, Entry> _toggles = new();

    public IEnumerable<WafToggle> GetAll() => _toggles.Values.Select(e => e.Toggle);

    public (WafToggle toggle, string etag)? Get(string rule)
        => _toggles.TryGetValue(rule, out var e) ? (e.Toggle, e.ETag) : null;

    public (WafToggle toggle, string etag) Add(WafToggle toggle)
    {
        var etag = Guid.NewGuid().ToString();
        _toggles[toggle.Rule] = new Entry(toggle, etag);
        return (toggle, etag);
    }

    public bool TryUpdate(string rule, WafToggle toggle, string? etag, out string newEtag, out WafToggle? before)
    {
        newEtag = Guid.NewGuid().ToString();
        before = null;
        if (!_toggles.TryGetValue(rule, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Toggle;
        return _toggles.TryUpdate(rule, new Entry(toggle, newEtag), existing);
    }

    public bool TryRemove(string rule, string? etag, out WafToggle? before)
    {
        before = null;
        if (!_toggles.TryGetValue(rule, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Toggle;
        return _toggles.TryRemove(rule, out _);
    }
}
