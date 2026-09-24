namespace CloudAlarmOverlay.Core.Models;

public sealed record AcknowledgementLog
{
    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public string? TaskSnapshotJson { get; init; }
    public string? TaskName { get; init; }
    public DateTime? ScheduledAt { get; init; }
    public string? Source { get; init; }
    public required string DeviceId { get; init; }
    public string? DisplayName { get; init; }
    public required DateTime TriggeredAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public int SnoozeCount { get; init; }
    public int? DurationSeconds { get; init; }
    public required string Result { get; init; }
    // Reserved for a later milestone; no setter or write operation in this version.
    public DateTime? SyncedAt => null;
    public string SyncStatus => "NotApplicable";
}
