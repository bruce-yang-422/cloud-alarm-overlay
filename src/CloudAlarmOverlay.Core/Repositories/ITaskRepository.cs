using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ITaskRepository
{
    Task<IReadOnlyList<AlarmTask>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AlarmTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task SaveLocalAsync(AlarmTask task, CancellationToken cancellationToken = default);
    Task DeleteLocalAsync(string id, CancellationToken cancellationToken = default);
    // A null time preserves the existing local time override; reset removes both overrides.
    Task SaveCalendarReminderAsync(string id, bool enabled, DateTime? scheduledAt, CancellationToken cancellationToken = default);
    Task ResetCalendarReminderAsync(string id, CancellationToken cancellationToken = default);
    Task ReplaceCloudCacheAsync(string source, IReadOnlyList<AlarmTask> tasks, CancellationToken cancellationToken = default);
}

