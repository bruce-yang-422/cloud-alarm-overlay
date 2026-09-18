namespace CloudAlarmOverlay.Core.Models;

public sealed record NotificationPolicy(string Level, bool RequireAcknowledgement);
public sealed record UpdateInfo(Version LatestVersion, Uri DownloadUrl, string ReleaseNote);
public enum ThemeMode { System, Light, Dark, Pink, Bamboo }


public enum ThemeColorStyle { Default, Pink, Bamboo, Lavender, Sunset, Silver }
