using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.ViewModels;

public sealed record ConnectionRow(string Name,string Kind,HealthStatus Health);
public sealed record ConnectionGroup(string Name,string Description,IReadOnlyList<ConnectionRow> Sources);
public static class ConnectionPresentation
{
    public static HealthStatus Aggregate(IEnumerable<HealthStatus> statuses)
    {
        var active=statuses.Where(s=>s.Severity!="Inactive").ToArray();
        var severity=active.Any(s=>s.Severity=="Error")?"Error":active.Any(s=>s.Severity=="Stopped")?"Stopped":active.Any(s=>s.Severity=="Pending")?"Pending":active.Length>0?"Healthy":"Inactive";
        var label=severity switch{"Error"=>"狀態異常","Stopped"=>"部分中斷","Pending"=>"尚待就緒","Healthy"=>"全部正常",_=>"未啟用"};
        return new(label,string.Join("\n",active.Select(s=>s.Detail)),severity);
    }
    public static IReadOnlyList<ConnectionGroup> GoogleGroups(GoogleWorkspaceSnapshot snapshot,DateTimeOffset now)
        =>snapshot.Accounts.Select(account=>new ConnectionGroup(account.Label,account.Email,
            snapshot.Sources.Where(s=>s.AccountId==account.Id).Select(source=>new ConnectionRow(source.Name,source.Kind=="Calendar"?"Google 日曆":"Google Sheets",ForSource(source,account,now)))
                .DefaultIfEmpty(new("尚未新增來源","Google 帳號",account.Status=="已連線"?new("已連線","從帳號管理新增試算表或日曆來源。","Inactive"):new("需重新登入",account.Status,"Error"))).ToArray())).ToArray();
    public static HealthStatus ForSource(GoogleSource source,GoogleAccount account,DateTimeOffset now)
    {
        if(!source.Enabled)return new("已停用","此來源已停止同步。","Inactive");
        var detail=$"最後成功：{source.LastSuccess?.ToLocalTime().ToString("MM/dd HH:mm")??"尚無"}";
        if(account.Status!="已連線")return new("需重新登入",account.Status+"\n"+detail,"Error");
        if(source.FailureCount>0)return new("同步失敗",source.Status+"\n"+detail+(source.RetryAfter is {} retry?$"\n預計重試：{retry.ToLocalTime():MM/dd HH:mm}":""),"Error");
        if(source.LastSuccess is null)return new("等待同步","此來源尚未完成首次同步。","Pending");
        if(now-source.LastSuccess>TimeSpan.FromMinutes(Math.Max(5,source.IntervalMinutes*3)))return new("資料過時",detail+"\n已超過預期同步時間。","Stopped");
        return new("正常",detail,"Healthy");
    }
}
