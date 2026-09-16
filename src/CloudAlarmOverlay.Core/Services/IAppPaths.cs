namespace CloudAlarmOverlay.Core.Services;

public interface IAppPaths
{
    string DataDirectory { get; }
    string DatabasePath { get; }
}

