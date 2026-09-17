using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IUpdateCheckService
{
    Task<UpdateInfo> GetManifestAsync(CancellationToken cancellationToken = default);
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);
}

