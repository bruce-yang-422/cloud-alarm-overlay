using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

public interface IUpdateInstallerService
{
    Task<DownloadedUpdate> DownloadAsync(UpdateInfo update,IProgress<UpdateDownloadProgress>? progress=null,CancellationToken ct=default);
    Task InstallAsync(DownloadedUpdate update,CancellationToken ct=default);
}

public interface IInstallerLauncher
{
    void Launch(string filePath);
}
