using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IAlarmService
{
    Task EnqueueAsync(AlarmTask task, CancellationToken cancellationToken = default);
}

