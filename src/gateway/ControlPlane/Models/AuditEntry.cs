namespace Gateway.ControlPlane.Models;

public record AuditEntry(
    DateTimeOffset Timestamp,
    string User,
    string Resource,
    string ResourceId,
    string Action,
    string? Before,
    string? After);
