using System.Collections.Concurrent;
using Gateway.ControlPlane.Models;

namespace Gateway.ControlPlane.Stores;

public interface IApiKeyStore
{
    IEnumerable<ApiKeyRecord> GetAll();
    (ApiKeyRecord key, string etag) Add(ApiKeyRecord key);
    bool TryUpdate(string id, ApiKeyRecord key, string? etag, out string newEtag, out ApiKeyRecord? before);
    bool TryRemove(string id, string? etag, out ApiKeyRecord? before);
    (ApiKeyRecord key, string etag)? Get(string id);
}

public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private sealed record Entry(ApiKeyRecord Key, string ETag);
    private readonly ConcurrentDictionary<string, Entry> _keys = new();

    public IEnumerable<ApiKeyRecord> GetAll() => _keys.Values.Select(e => e.Key);

    public (ApiKeyRecord key, string etag)? Get(string id)
        => _keys.TryGetValue(id, out var e) ? (e.Key, e.ETag) : null;

    public (ApiKeyRecord key, string etag) Add(ApiKeyRecord key)
    {
        var etag = Guid.NewGuid().ToString();
        _keys[key.Id] = new Entry(key, etag);
        return (key, etag);
    }

    public bool TryUpdate(string id, ApiKeyRecord key, string? etag, out string newEtag, out ApiKeyRecord? before)
    {
        newEtag = Guid.NewGuid().ToString();
        before = null;
        if (!_keys.TryGetValue(id, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Key;
        return _keys.TryUpdate(id, new Entry(key, newEtag), existing);
    }

    public bool TryRemove(string id, string? etag, out ApiKeyRecord? before)
    {
        before = null;
        if (!_keys.TryGetValue(id, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Key;
        return _keys.TryRemove(id, out _);
    }
}
