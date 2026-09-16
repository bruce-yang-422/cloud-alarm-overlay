using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
public sealed class UpdateCheckService(ISettingsRepository settings, HttpClient client) : IUpdateCheckService
{
    public static Version CurrentVersion => typeof(UpdateCheckService).Assembly.GetName().Version ?? new Version(1,0,0);
    public static Uri ValidateUrl(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||uri.UserInfo.Length>0)
            throw new ArgumentException("請輸入公開 HTTPS 連結。");
        if(uri.Host=="drive.google.com" && uri.AbsolutePath.StartsWith("/file/d/",StringComparison.Ordinal))
        {
            var id=uri.AbsolutePath.Split('/')[3];
            return new Uri("https://drive.google.com/uc?export=download&id="+Uri.EscapeDataString(id));
        }
        return uri;
    }
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken=default)
    {
        var value=(await settings.GetAsync("UpdateManifestUrl",cancellationToken))?.Value;
        if(string.IsNullOrWhiteSpace(value))throw new InvalidOperationException("尚未設定更新資訊連結，請由管理者設定 version.json 連結。");
        using var request=new HttpRequestMessage(HttpMethod.Get,ValidateUrl(value));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var body=await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer=new MemoryStream(); var bytes=new byte[4096];
        int count;
        while((count=await body.ReadAsync(bytes,timeout.Token))>0)
        { if(buffer.Length+count>65536)throw new InvalidDataException("更新資訊超過 64 KB。"); await buffer.WriteAsync(bytes.AsMemory(0,count),timeout.Token); }
        using var json=JsonDocument.Parse(buffer.ToArray());
        var root=json.RootElement;
        if(!root.TryGetProperty("latestVersion",out var version)||!Version.TryParse(version.GetString(),out var latest))throw new InvalidDataException("version.json 版本格式錯誤。");
        var normalized=new Version(latest.Major,latest.Minor,Math.Max(0,latest.Build),Math.Max(0,latest.Revision));
        if(normalized<=CurrentVersion)return null;
        if(!root.TryGetProperty("downloadUrl",out var download))throw new InvalidDataException("更新資訊缺少下載連結。");
        return new(latest,ValidateUrl(download.GetString()??""),root.TryGetProperty("releaseNote",out var note)?note.GetString()??"":"");
    }
}