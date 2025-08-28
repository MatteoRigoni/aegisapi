using System.Collections.Concurrent;
using Gateway.ControlPlane.Models;

namespace Gateway.ControlPlane.Stores;

public interface IRouteStore
{
    IEnumerable<RouteConfig> GetAll();
    (RouteConfig route, string etag) Add(RouteConfig route);
    bool TryUpdate(string id, RouteConfig route, string? etag, out string newEtag, out RouteConfig? before);
    bool TryRemove(string id, string? etag, out RouteConfig? before);
    (RouteConfig route, string etag)? Get(string id);
    event Action? Changed;
}

public sealed class InMemoryRouteStore : IRouteStore
{
    private sealed record Entry(RouteConfig Route, string ETag);
    private readonly ConcurrentDictionary<string, Entry> _routes = new();

    public event Action? Changed;

    public InMemoryRouteStore(IEnumerable<RouteConfig>? seed = null)
    {
        if (seed != null)
        {
            foreach (var route in seed)
            {
                var etag = Guid.NewGuid().ToString();
                _routes[route.Id] = new Entry(route, etag);
            }
        }
    }

    public IEnumerable<RouteConfig> GetAll() => _routes.Values.Select(e => e.Route);

    public (RouteConfig route, string etag)? Get(string id)
        => _routes.TryGetValue(id, out var e) ? (e.Route, e.ETag) : null;

    public (RouteConfig route, string etag) Add(RouteConfig route)
    {
        var etag = Guid.NewGuid().ToString();
        _routes[route.Id] = new Entry(route, etag);
        Changed?.Invoke();
        return (route, etag);
    }

    public bool TryUpdate(string id, RouteConfig route, string? etag, out string newEtag, out RouteConfig? before)
    {
        newEtag = Guid.NewGuid().ToString();
        before = null;
        if (!_routes.TryGetValue(id, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Route;
        var updated = _routes.TryUpdate(id, new Entry(route, newEtag), existing);
        if (updated) Changed?.Invoke();
        return updated;
    }

    public bool TryRemove(string id, string? etag, out RouteConfig? before)
    {
        before = null;
        if (!_routes.TryGetValue(id, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Route;
        var removed = _routes.TryRemove(id, out _);
        if (removed) Changed?.Invoke();
        return removed;
    }
}
