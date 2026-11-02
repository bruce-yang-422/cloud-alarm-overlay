using System.Text.RegularExpressions;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

public static class UpdateDownloadPolicy
{
    public static bool IsSha256(string value)=>Regex.IsMatch(value,@"\A[0-9a-fA-F]{64}\z");
    public static void Validate(UpdateInfo update)
    {
        if(!IsSha256(update.Sha256))throw new InvalidDataException("更新資訊缺少有效的 SHA-256，無法下載或執行安裝檔。");
        ValidateUrl(update.DownloadUrl,false);
    }
    public static void ValidateUrl(Uri uri,bool redirected)
    {
        if(!uri.IsAbsoluteUri||uri.Scheme!=Uri.UriSchemeHttps||uri.Port!=443||uri.UserInfo.Length>0||uri.Fragment.Length>0)
            throw new InvalidDataException("安裝檔下載僅允許 GitHub Releases 的 HTTPS 連結。");
        if(uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)&&uri.Query.Length==0&&
            Regex.IsMatch(uri.AbsolutePath,@"\A/[^/]+/[^/]+/releases/download/[^/]+/[^/]+\.exe\z",RegexOptions.IgnoreCase))return;
        // GitHub sends release downloads to its signed asset CDN. Validate each hop
        // before sending it; never enable HttpClient's automatic redirects.
        if(redirected&&(uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com")&&
            uri.AbsolutePath.StartsWith("/github-production-release-asset",StringComparison.Ordinal))return;
        throw new InvalidDataException("安裝檔下載或重新導向的來源不在 GitHub Releases 允許清單內。");
    }
}
