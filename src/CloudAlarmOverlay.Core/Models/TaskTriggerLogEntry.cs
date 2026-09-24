namespace CloudAlarmOverlay.Core.Models;

/// <summary>
/// One row per Occurrences record, left-joined with its AcknowledgementLogs counterpart (same Id).
/// Unlike AcknowledgementLog alone, this surfaces occurrences that were claimed/displayed but never
/// reached a logged result (e.g. the app crashed between DisplayedAsync and CompleteAsync), so a
/// per-task export shows every scheduled firing, not just the ones that finished cleanly.
/// </summary>
public sealed record TaskTriggerLogEntry
{
    public required string OccurrenceId { get; init; }
    public required string TaskId { get; init; }
    public string? TaskName { get; init; }
    public string? Source { get; init; }
    public required DateTime ScheduledAt { get; init; }
    /// <summary>Occurrences.State: Claimed / Displayed / Snoozed / Acknowledged / Missed.</summary>
    public required string OccurrenceState { get; init; }
    public DateTime? TriggeredAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public int SnoozeCount { get; init; }
    public int? DurationSeconds { get; init; }
    /// <summary>Null when Occurrences has no matching AcknowledgementLogs row at all (true firing failure).</summary>
    public string? Result { get; init; }
}
