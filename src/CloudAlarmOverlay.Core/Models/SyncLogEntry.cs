namespace CloudAlarmOverlay.Core.Models;

public sealed record SyncLogEntry
{
    public DateTime? LastSeenAt {get;init;}
    public int RepeatCount {get;init;}=1;
    public string EventKind {get;init;}="Legacy";
    public long Id { get; init; }
    public required DateTime Time { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
    public int? RecordCount { get; init; }
    public string? Source { get; init; }
}

