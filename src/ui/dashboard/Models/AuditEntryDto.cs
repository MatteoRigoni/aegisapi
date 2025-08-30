namespace Dashboard.Models;

public record AuditEntryDto(DateTime Timestamp, string Action, string Subject);
