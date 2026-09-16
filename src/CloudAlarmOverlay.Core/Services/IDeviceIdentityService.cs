using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IDeviceIdentityService
{
    Task<Device?> GetLocalAsync(CancellationToken cancellationToken = default);
    Task SetInitialIdentityAsync(string deviceId, string displayName, CancellationToken cancellationToken = default);
}

