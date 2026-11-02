namespace CloudAlarmOverlay.Core.Models;

public sealed record NotificationPolicy(string Level, bool RequireAcknowledgement);
public sealed record UpdateInfo(Version LatestVersion, Uri DownloadUrl, string ReleaseNote)
{
    public string Sha256 {get;init;}="";
}
public sealed record DownloadedUpdate(UpdateInfo Info,string FilePath);
public sealed record UpdateDownloadProgress(long BytesReceived,long? TotalBytes,bool IsVerifying=false);
public enum ThemeMode { System, Light, Dark, Pink, Bamboo }


public enum ThemeColorStyle { Default, Pink, Bamboo, Lavender, Sunset, Silver }
