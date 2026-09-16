using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IHolidayService
{
    Task<bool> ShouldTriggerAsync(AlarmTask task, DateOnly date, bool recurrenceMatches, CancellationToken cancellationToken = default);
}

