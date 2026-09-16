using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ISyncService
{
    Task SyncAsync(CancellationToken cancellationToken = default);
}

