using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ITaskSchedulingService
{
    Task<DateTime?> GetNextOccurrenceAsync(AlarmTask task, DateTime after, CancellationToken cancellationToken = default);
}

