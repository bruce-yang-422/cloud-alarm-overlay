namespace CloudAlarmOverlay.Core.Models;

public sealed record PomodoroLogEntry
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required DateTime StartedAt { get; init; }
    // Schema §21 defines NULL for interrupted sessions.
    public DateTime? CompletedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public int PlannedMinutes { get; init; }
    public bool IsActive { get; init; }
    public required string Result { get; init; }
}
