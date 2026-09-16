using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Milestone 0 placeholder; never reports an unperformed operation as successful.</summary>
internal sealed class HolidayService : IHolidayService
{
    public Task<bool> ShouldTriggerAsync(AlarmTask task, DateOnly date, bool recurrenceMatches, CancellationToken cancellationToken = default)
        => throw new MilestoneNotImplementedException("HolidayService.ShouldTriggerAsync");
}

