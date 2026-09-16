using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ITaskService
{
    Task SaveLocalAsync(AlarmTask task, CancellationToken cancellationToken = default);
    Task DeleteLocalAsync(string id, CancellationToken cancellationToken = default);
}

