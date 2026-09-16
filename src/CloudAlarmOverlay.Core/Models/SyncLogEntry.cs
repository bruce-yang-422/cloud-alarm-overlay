namespace CloudAlarmOverlay.Core.Models;

public sealed record SyncLogEntry
{
    public long Id { get; init; }
    public required DateTime Time { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
    public int? RecordCount { get; init; }
    public string? Source { get; init; }
}

