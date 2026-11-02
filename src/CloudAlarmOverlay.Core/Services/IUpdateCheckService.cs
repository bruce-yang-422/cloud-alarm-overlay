using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Reads release metadata and checks whether a newer version is available.</summary>
public interface IUpdateCheckService
{
    Task<UpdateInfo> GetManifestAsync(CancellationToken cancellationToken = default);
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);
}

