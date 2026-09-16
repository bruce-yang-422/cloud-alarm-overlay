using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Milestone 0 placeholder; never reports an unperformed operation as successful.</summary>
internal sealed class LunarScheduleService : ILunarScheduleService
{
    public Task<bool> MatchesAsync(string recurrence, DateOnly date, CancellationToken cancellationToken = default)
        => throw new MilestoneNotImplementedException("LunarScheduleService.MatchesAsync");
}

