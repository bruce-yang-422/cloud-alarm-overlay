using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Milestone 0 placeholder; never reports an unperformed operation as successful.</summary>
internal sealed class OverdueGraceService : IOverdueGraceService
{
    public Task CheckAsync(DateTime now, CancellationToken cancellationToken = default)
        => throw new MilestoneNotImplementedException("OverdueGraceService.CheckAsync");
}

