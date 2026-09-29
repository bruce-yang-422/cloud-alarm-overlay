using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IDeviceRepository
{
    Task<Device?> GetLocalAsync(CancellationToken cancellationToken = default);
    Task SaveLocalAsync(Device device, CancellationToken cancellationToken = default);
    Task SaveInitialAsync(Device device, string usageMode, CancellationToken cancellationToken = default);
}

