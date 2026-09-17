using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

public sealed record HealthStatus(string Label,string Detail,string Severity);
public interface IAlarmHeartbeat
{
    void Tick();
    void Fail(string reason);
    void Stop();
    HealthStatus GetStatus(DateTime now);
}
public sealed class AlarmHeartbeat(ChangeSignal signal,TimeProvider clock):IAlarmHeartbeat
{
    private readonly object gate=new();
    private DateTime? lastTick;
    private string? error;
    private bool stopped;
    public void Tick()
    {
        bool changed;
        lock(gate){changed=lastTick is null||error is not null||stopped;lastTick=clock.GetLocalNow().DateTime;error=null;stopped=false;}
        // Wake only the UI: waking AlarmWorker from its own tick would cause a busy loop.
        if(changed)signal.Notify(wakeScheduler:false);
    }
    public void Fail(string reason){lock(gate)error=reason;signal.Notify(wakeScheduler:false);}
    public void Stop(){lock(gate)stopped=true;signal.Notify(wakeScheduler:false);}
    public HealthStatus GetStatus(DateTime now)
    {
        lock(gate)
        {
            var detail=lastTick is {} at?$"最後排程檢查：{at:yyyy/MM/dd HH:mm:ss}":"尚未完成排程檢查";
            if(stopped||(lastTick is {} tick&&now-tick>TimeSpan.FromMinutes(2)))return new("中斷",detail+(error is null?"":"\n"+error),"Stopped");
            if(error is not null)return new("異常",detail+"\n"+error,"Error");
            return lastTick is null?new("等待啟動",detail,"Pending"):new("正常",detail,"Healthy");
        }
    }
}
public static class SyncFingerprint
{
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Configuration(string source,SyncOptions options,Device device)
    {
        var id=source.StartsWith("SheetA/",StringComparison.Ordinal)?options.SheetAId:options.SheetBId;
        var gid=source switch{"SheetA/Tasks"=>options.TasksAGid,"SheetA/Employees"=>options.EmployeesGid,"SheetA/Holidays"=>options.HolidaysGid,"SheetA/LunarCalendar"=>options.LunarGid,_=>options.TasksBGid};
        return Hash(JsonSerializer.Serialize(new{id,gid,options.IntervalSeconds,device.DeviceId,device.DisplayName}));
    }
}
public static class SyncHealth
{
    public static HealthStatus ForSheet(string sheet,SyncOptions options,Device? device,IReadOnlyList<SyncState> states,DateTime now)
    {
        if((sheet=="SheetA"?options.SheetAId:options.SheetBId)=="")return new("未設定","尚未設定同步來源","Pending");
        if(device is null)return new("等待設定","請先設定裝置身分","Pending");
        string[] sources=sheet=="SheetA"?["SheetA/Employees","SheetA/Holidays","SheetA/LunarCalendar","SheetA/Tasks"]:["SheetB/Tasks"];
        var current=sources.Select(source=>states.FirstOrDefault(s=>s.Source==source&&s.ConfigFingerprint==SyncFingerprint.Configuration(source,options,device))).ToArray();
        if(current.Any(s=>s is null))return new("等待同步","目前設定尚未完成所有分頁檢查","Pending");
        var checks=current.Select(s=>s!).ToArray();
        var detail=string.Join("\n",checks.Select(s=>$"{s.Source}：{s.Status} · 檢查 {s.LastCheckedAt:MM/dd HH:mm:ss} · 最後成功 {s.LastSuccessAt?.ToString("MM/dd HH:mm:ss")??"尚無"}"+(s.Status=="成功"?"":"\n"+s.Message)));
        var timeout=TimeSpan.FromSeconds(Math.Clamp(options.IntervalSeconds,30,60)*3);
        if(checks.Any(s=>now-s.LastCheckedAt>timeout || (s.Status!="成功"&&s.LastSuccessAt is {} at&&now-at>timeout)))return new("中斷",detail,"Stopped");
        return checks.Any(s=>s.Status!="成功")?new("異常",detail,"Error"):new("正常",detail,"Healthy");
    }
}
