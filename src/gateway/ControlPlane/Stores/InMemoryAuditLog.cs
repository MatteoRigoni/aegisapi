using System.Collections.Concurrent;
using Gateway.ControlPlane.Models;
using System.Text.Json;

namespace Gateway.ControlPlane.Stores;

public interface IAuditLog
{
    void Log(string user, string resource, string id, string action, object? before, object? after);
    IEnumerable<AuditEntry> GetAll();
}

public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public void Log(string user, string resource, string id, string action, object? before, object? after)
    {
        var entry = new AuditEntry(
            DateTimeOffset.UtcNow,
            user,
            resource,
            id,
            action,
            before is null ? null : JsonSerializer.Serialize(before),
            after is null ? null : JsonSerializer.Serialize(after));
        _entries.Enqueue(entry);
    }

    public IEnumerable<AuditEntry> GetAll() => _entries.ToArray();
}
