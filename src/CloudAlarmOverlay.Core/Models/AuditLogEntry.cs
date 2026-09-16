namespace CloudAlarmOverlay.Core.Models;

public sealed record AuditLogEntry
{
    public long Id { get; init; }
    public required string UserId { get; init; }
    public required string Action { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public required DateTime CreatedAt { get; init; }
}

