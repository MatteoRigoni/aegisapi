using System.Collections.Concurrent;
using Gateway.ControlPlane.Models;

namespace Gateway.ControlPlane.Stores;

public interface ISchemaStore
{
    IEnumerable<SchemaRecord> GetAll();
    (SchemaRecord schema, string etag)? Get(string path);
    (SchemaRecord schema, string etag) Add(SchemaRecord schema);
    bool TryUpdate(string path, SchemaRecord schema, string? etag, out string newEtag, out SchemaRecord? before);
    bool TryRemove(string path, string? etag, out SchemaRecord? before);
}

public sealed class InMemorySchemaStore : ISchemaStore
{
    private sealed record Entry(SchemaRecord Schema, string ETag);
    private readonly ConcurrentDictionary<string, Entry> _schemas = new();

    public IEnumerable<SchemaRecord> GetAll() => _schemas.Values.Select(e => e.Schema);

    public (SchemaRecord schema, string etag)? Get(string path)
        => _schemas.TryGetValue(path, out var e) ? (e.Schema, e.ETag) : null;

    public (SchemaRecord schema, string etag) Add(SchemaRecord schema)
    {
        var etag = Guid.NewGuid().ToString();
        _schemas[schema.Path] = new Entry(schema, etag);
        return (schema, etag);
    }

    public bool TryUpdate(string path, SchemaRecord schema, string? etag, out string newEtag, out SchemaRecord? before)
    {
        newEtag = Guid.NewGuid().ToString();
        before = null;
        if (!_schemas.TryGetValue(path, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Schema;
        return _schemas.TryUpdate(path, new Entry(schema, newEtag), existing);
    }

    public bool TryRemove(string path, string? etag, out SchemaRecord? before)
    {
        before = null;
        if (!_schemas.TryGetValue(path, out var existing) || existing.ETag != etag)
            return false;
        before = existing.Schema;
        return _schemas.TryRemove(path, out _);
    }
}
