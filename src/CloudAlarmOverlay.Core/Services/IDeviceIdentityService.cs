using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IDeviceIdentityService
{
    Task<Device?> GetLocalAsync(CancellationToken cancellationToken = default);
    Task SetInitialIdentityAsync(string deviceId, string displayName, CancellationToken cancellationToken = default);
    Task SetInitialIdentityAsync(string deviceId, string displayName, string usageMode, CancellationToken cancellationToken = default);
    Task<string> GetUsageModeAsync(CancellationToken cancellationToken = default);
    Task InitializeAccessAsync(CancellationToken cancellationToken = default);
}

