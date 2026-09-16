using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IAckLogRepository
{
    Task<IReadOnlyList<AcknowledgementLog>> GetRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task SaveAsync(AcknowledgementLog entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every Occurrences row for a task (optionally date-bounded by ScheduledAt), left-joined with its
    /// AcknowledgementLogs counterpart. Includes occurrences that never produced a log row, so a firing
    /// failure (claimed/displayed but no result ever recorded) is visible instead of silently absent.
    /// </summary>
    Task<IReadOnlyList<TaskTriggerLogEntry>> GetTriggerLogAsync(string taskId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}

