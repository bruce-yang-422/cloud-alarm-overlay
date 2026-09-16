using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.Infrastructure;

public sealed class AppPaths : IAppPaths
{
    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudAlarmOverlay");

    public string DatabasePath => Path.Combine(DataDirectory, "cloud_alarm_overlay.db");
}

