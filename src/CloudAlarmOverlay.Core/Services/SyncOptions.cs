using System.Text.Json;
using System.Text.RegularExpressions;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
public sealed record SyncOptions
{
    public string SheetAId {get;init;}="";
    public string TasksAGid {get;init;}="";
    public string HolidaysGid {get;init;}="";
    public string EmployeesGid {get;init;}="";
    public string LunarGid {get;init;}="";
    public string SheetBId {get;init;}="";
    public string TasksBGid {get;init;}="";
    public int IntervalSeconds {get;init;}=45;
    public void Validate()
    {
        if(IntervalSeconds is <30 or >60)throw new ArgumentException("同步間隔必須介於 30–60 秒。");
        foreach(var id in new[]{SheetAId,SheetBId}) if(id!=""&&!Regex.IsMatch(id,@"^[a-zA-Z0-9_-]+$"))throw new ArgumentException("請填入 Spreadsheet ID，不是完整網址。");
        foreach(var gid in new[]{TasksAGid,HolidaysGid,EmployeesGid,LunarGid,TasksBGid})
            if(gid!=""&&!Regex.IsMatch(gid,@"^\d+$"))throw new ArgumentException("GID 必須為數字。");
        if(SheetAId!=""&&new[]{TasksAGid,HolidaysGid,EmployeesGid,LunarGid}.Any(string.IsNullOrEmpty))throw new ArgumentException("Sheet A 請完整填寫四個分頁 GID。");
        if(SheetBId!=""&&TasksBGid=="")throw new ArgumentException("請填寫 Sheet B 任務 GID。");
    }
}
public sealed class SyncConfiguration(ISettingsRepository settings, IAdminSettingsStore admin, AdminSession session)
{
    public async Task<SyncOptions> LoadAsync(CancellationToken ct=default)
    {
        var json=(await settings.GetAsync("SyncOptions",ct))?.Value;
        return json is null?new():JsonSerializer.Deserialize<SyncOptions>(json)??new();
    }
    public async Task SaveAsync(SyncOptions options,CancellationToken ct=default)
    {
        options.Validate();
        session.RequireAdmin();
        if((await settings.GetAsync("SyncLinksLocked",ct))?.Value=="true")throw new InvalidOperationException("請先解除同步來源鎖定。");
        await admin.SaveAsync([new Setting{Key="SyncOptions",Value=JsonSerializer.Serialize(options)}],ct);
    }
}
public interface ISheetCsvClient
{
    Task<string> DownloadAsync(string spreadsheetId,string gid,CancellationToken ct=default);
}
