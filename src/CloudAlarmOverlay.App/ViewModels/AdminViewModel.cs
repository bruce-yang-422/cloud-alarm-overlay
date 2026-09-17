using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class AdminViewModel(AdminSession session,IAdminSettingsStore store,ISettingsRepository settings,
    IAuditLogRepository audit,IAuditService auditService,ISyncLogRepository syncLogs,
    ISheetCsvClient client,ICsvSheetParser parser,IUserDialogs dialogs,PreferencesViewModel preferences,
    ISystemEventStore events,IExitProtectionService exitProtection,IAutoStartService autoStart,IAuthenticationService authentication):ObservableObject
{
    public AdminSession Session=>session;
    public PreferencesViewModel Preferences=>preferences;
    [ObservableProperty] private bool isAuthenticated;
    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private string message="";
    [ObservableProperty] private string adminUsername="";
    [ObservableProperty] private string sheetATestState="尚未測試";
    [ObservableProperty] private string sheetATestedAt="—";
    [ObservableProperty] private string sheetATasksStatus="等待測試";
    [ObservableProperty] private string sheetAHolidaysStatus="等待測試";
    [ObservableProperty] private string sheetAEmployeesStatus="等待測試";
    [ObservableProperty] private string sheetALunarStatus="等待測試";
    [ObservableProperty] private string sheetBTestState="尚未測試";
    [ObservableProperty] private string sheetBTestedAt="—";
    [ObservableProperty] private string sheetBTasksStatus="等待測試";
    [ObservableProperty] private bool lockFlash;
    [ObservableProperty] private bool lockQuiet;
    [ObservableProperty] private bool linksLocked;
    [ObservableProperty] private string exitPasswordModeLabel="目前方式：管理員帳號與密碼";
    [ObservableProperty] private bool exitPasswordRequired=true;
    [ObservableProperty] private bool autoStartEnabled;
    public bool LinksEditable=>!LinksLocked;
    public string LinkState=>LinksLocked?"🔒 同步連結已鎖定":"同步連結可編輯";
    public string LinkToggleText=>LinksLocked?"解鎖連結":"鎖定連結";
    partial void OnLinksLockedChanged(bool value){OnPropertyChanged(nameof(LinkState));OnPropertyChanged(nameof(LinkToggleText));}
    public bool ShowsLogFilter=>SelectedTab is 2 or 3;
    partial void OnSelectedTabChanged(int value)=>OnPropertyChanged(nameof(ShowsLogFilter));
    [ObservableProperty] private int flashMilliseconds=500;
    [ObservableProperty] private DateTime from=DateTime.Today.AddDays(-29);
    [ObservableProperty] private DateTime to=DateTime.Today;
    public ObservableCollection<AuditLogEntry> Audit {get;}=[];
    public ObservableCollection<SyncLogEntry> Logs {get;}=[];
    public ObservableCollection<SystemEventEntry> Events {get;}=[];
    public event Action? LoginRequested;
    private string? previousActor;
    public bool IsSignedOut=>!IsAuthenticated;
    public string Version=>typeof(AdminViewModel).Assembly.GetName().Version?.ToString()??"";
    public async Task SessionChangedAsync()
    {
        var actor=session.Username;
        var old=previousActor;previousActor=actor;
        IsAuthenticated=actor is not null;AdminUsername=actor??"";OnPropertyChanged(nameof(IsSignedOut));
        if(!IsAuthenticated)
        {
            Audit.Clear();Logs.Clear();Events.Clear();Message="已登出管理者模式。";
            if(old is not null)await auditService.RecordAsync(new AuditLogEntry{UserId=old,Action="管理者登出／閒置逾時",CreatedAt=DateTime.Now});
            return;
        }
        await LoadAsync();
    }
    public async Task LoadAsync()
    {
        try
        {
            session.RequireAdmin();
            await preferences.LoadAsync();
            LockFlash=preferences.FlashLocked;LockQuiet=preferences.QuietLocked;FlashMilliseconds=preferences.FlashMilliseconds;
            LinksLocked=(await settings.GetAsync("SyncLinksLocked"))?.Value=="true";
            await RefreshExitPasswordModeAsync();
            ExitPasswordRequired=await exitProtection.IsRequiredAsync();
            AutoStartEnabled=autoStart.IsEnabled();
            OnPropertyChanged(nameof(LinksEditable));
            await RefreshLogsAsync();
        }
        catch(Exception ex){Message=ex.Message;}
    }
    [RelayCommand] private void Login()=>LoginRequested?.Invoke();
    [RelayCommand] private void Logout()=>session.SignOut();
    public Task ChangeCredentialsAsync(string currentPassword,string newPassword)
        =>authentication.ChangeCredentialsAsync(currentPassword,AdminUsername,newPassword);
    private async Task RefreshExitPasswordModeAsync()
        =>ExitPasswordModeLabel=await exitProtection.GetModeAsync()=="Dedicated"?"目前方式：專用結束密碼":"目前方式：管理員帳號與密碼";
    public async Task SaveExitPasswordRequirementAsync()
    {
        try
        {
            session.RequireAdmin();
            await exitProtection.SetRequiredAsync(ExitPasswordRequired);
            Message=ExitPasswordRequired?"結束程式密碼保護已啟用。":"結束程式密碼保護已關閉。";
        }
        catch(Exception ex){ExitPasswordRequired=await exitProtection.IsRequiredAsync();Message=ex.Message;}
    }
    public void SaveAutoStart()
    {
        try
        {
            autoStart.SetEnabled(AutoStartEnabled);
            Message=AutoStartEnabled?"Windows 登入後自動啟動已開啟。":"Windows 登入後自動啟動已關閉。";
        }
        catch(Exception ex){AutoStartEnabled=autoStart.IsEnabled();Message=ex.Message;}
    }
    public async Task SetExitPasswordAsync(string password)
    {
        session.RequireAdmin();
        await exitProtection.SetDedicatedPasswordAsync(password);
        await RefreshExitPasswordModeAsync();
        await RefreshLogsAsync();
        Message=ExitPasswordRequired?"專用結束密碼已設定。下次結束程式時需輸入此密碼。":"專用結束密碼已設定；目前保護已關閉，開啟後才會要求密碼。";
    }
    public async Task UseAdministratorExitPasswordAsync()
    {
        session.RequireAdmin();
        await exitProtection.UseAdministratorPasswordAsync();
        await RefreshExitPasswordModeAsync();
        await RefreshLogsAsync();
        Message=ExitPasswordRequired?"已改用管理員帳號與密碼驗證結束程式。":"已選用管理員帳號與密碼；目前保護已關閉，開啟後才會要求驗證。";
    }
    [RelayCommand] private void OpenTab(string tab)
    {
        session.RequireAdmin();SelectedTab=int.Parse(tab);
    }
    [RelayCommand] private async Task SavePolicyAsync()
    {
        try
        {
            await store.SaveAsync([
                new Setting{Key="FlashMilliseconds",Value=FlashMilliseconds.ToString(),Locked=LockFlash},
                new Setting{Key="QuietPeriods",Value=(await settings.GetAsync("QuietPeriods"))?.Value??"[]",Locked=LockQuiet}]);
            await preferences.LoadAsync();await RefreshLogsAsync();Message="本機鎖定設定已儲存。";
        }
        catch(Exception ex){Message=ex.Message;}
    }
    [RelayCommand] private async Task ToggleLinksAsync()
    {
        try
        {
            var next=!LinksLocked;
            await store.SaveAsync([new Setting{Key="SyncLinksLocked",Value=next?"true":"false"}]);
            LinksLocked=next;OnPropertyChanged(nameof(LinksEditable));Message=next?"同步來源已鎖定。":"同步來源已解鎖。";
        }
        catch(Exception ex){Message=ex.Message;}
    }
    private bool logsLoaded;
    [RelayCommand] private async Task RefreshLogsAsync()
    {
        try
        {
            session.RequireAdmin();
            if(From.Date>To.Date)throw new ArgumentException("起始日期不能晚於結束日期。");
            logsLoaded=false;Message="正在讀取紀錄…";
            var end=To.Date.AddDays(1).AddTicks(-1);
            var a=await audit.GetRangeAsync(From.Date,end);
            var s=await syncLogs.GetRangeAsync(From.Date,end);
            var ev=await events.GetRangeAsync(From.Date,end);
            session.RequireAdmin();
            Audit.Clear();foreach(var row in a)Audit.Add(row);
            Logs.Clear();foreach(var row in s)Logs.Add(row);
            Events.Clear();foreach(var row in ev)Events.Add(row);
            logsLoaded=true;Message=$"已載入 {Audit.Count} 筆稽核、{Logs.Count} 筆同步與 {Events.Count} 筆系統事件。";
        }
        catch(Exception ex){Message=ex.Message;}
    }
    public async Task TestConnectionAsync(SyncOptions options,bool sheetA)
    {
        var source=sheetA?"Sheet A":"Sheet B";
        try
        {
            session.RequireAdmin();
            BeginConnectionTest(sheetA);
            var id=sheetA?options.SheetAId:options.SheetBId;
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("請先填寫要測試的 Spreadsheet ID。");
            var tabs=sheetA?new[]{("Tasks",options.TasksAGid),("Holidays",options.HolidaysGid),("Employees",options.EmployeesGid),("LunarCalendar",options.LunarGid)}:new[]{("Tasks",options.TasksBGid)};
            var results=await Task.WhenAll(tabs.Select(async t=>{
                try
                {
                    var csv=await client.DownloadAsync(id,t.Item2);
                    int count=t.Item1 switch{
                        "Tasks"=>parser.ParseTasks(csv,sheetA?TaskSources.SheetA:TaskSources.SheetB).Count,
                        "Holidays"=>parser.ParseHolidays(csv).Count,
                        "Employees"=>parser.ParseEmployees(csv).Count,_=>parser.ParseLunarCalendar(csv).Count};
                    return new ConnectionTestResult(t.Item1,true,count,"");
                }
                catch(Exception ex){return new ConnectionTestResult(t.Item1,false,null,ex.Message);}
            }));
            session.RequireAdmin();
            ApplyConnectionResults(sheetA,results);
            var succeeded=results.Count(r=>r.Success);
            Message=succeeded==results.Length
                ?$"{source} 連線測試成功；測試未變更快取。"
                :$"{source} 連線測試完成：{succeeded}/{results.Length} 個分頁成功；測試未變更快取。";
        }
        catch(Exception ex){Message=ex.Message;FailConnectionTest(sheetA,ex.Message);}
    }
    private void BeginConnectionTest(bool sheetA)
    {
        if(sheetA)
        {
            SheetATestState="測試中";SheetATestedAt="正在下載及檢查公開 CSV…";
            SheetATasksStatus=SheetAHolidaysStatus=SheetAEmployeesStatus=SheetALunarStatus="檢查中…";
        }
        else {SheetBTestState="測試中";SheetBTestedAt="正在下載及檢查公開 CSV…";SheetBTasksStatus="檢查中…";}
    }
    private void ApplyConnectionResults(bool sheetA,IReadOnlyCollection<ConnectionTestResult> results)
    {
        static string Display(ConnectionTestResult result)=>result.Success
            ?$"成功 · {result.Count} 筆 · 欄位完整"
            :$"失敗 · {result.Error}";
        var succeeded=results.Count(r=>r.Success);
        var state=succeeded==results.Count?"成功":succeeded==0?"失敗":"部分失敗";
        var time=$"最後測試：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        if(sheetA)
        {
            SheetATestState=state;SheetATestedAt=time;
            foreach(var result in results)
                switch(result.Name)
                {
                    case "Tasks":SheetATasksStatus=Display(result);break;
                    case "Holidays":SheetAHolidaysStatus=Display(result);break;
                    case "Employees":SheetAEmployeesStatus=Display(result);break;
                    case "LunarCalendar":SheetALunarStatus=Display(result);break;
                }
        }
        else
        {
            SheetBTestState=state;SheetBTestedAt=time;
            SheetBTasksStatus=Display(results.Single());
        }
    }
    private void FailConnectionTest(bool sheetA,string error)
    {
        var time=$"最後測試：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        if(sheetA){SheetATestState="失敗";SheetATestedAt=time;SheetATasksStatus="失敗 · "+error;}
        else {SheetBTestState="失敗";SheetBTestedAt=time;SheetBTasksStatus="失敗 · "+error;}
    }
    private sealed record ConnectionTestResult(string Name,bool Success,int? Count,string Error);
    [RelayCommand] private async Task ExportAsync(string kind)
    {
        await RefreshLogsAsync();
        try
        {
            session.RequireAdmin();
            if(From.Date>To.Date||!logsLoaded)return;
            string csv=kind switch{
                "AuditLog"=>CsvExport.Build(["CreatedAt","UserId","Action","OldValue","NewValue"],
                    Audit.Select(r=>new[]{CsvExport.Date(r.CreatedAt),r.UserId,r.Action,r.OldValue,r.NewValue})),
                "SystemEvent"=>CsvExport.Build(["Time","EventType","Message"],Events.Select(r=>new[]{CsvExport.Date(r.Time),r.EventType,r.Message})),
                _=>CsvExport.Build(["Time","Source","Status","Message","RecordCount"],Logs.Select(r=>new[]{CsvExport.Date(r.Time),r.Source,r.Status,r.Message,r.RecordCount?.ToString()}))};
            dialogs.ExportNamed(csv,$"{kind}_{From:yyyyMMdd}_{To:yyyyMMdd}.csv");
            Message="匯出對話框已開啟。";
        }
        catch(Exception ex){Message=ex.Message;}
    }
}
public static class CsvExport
{
    public static string Date(DateTime date)=>date.ToString("yyyy-MM-dd HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture);
    public static string Build(string[] headers,IEnumerable<string?[]> rows)=>
        string.Join("\r\n",new[]{string.Join(",",headers)}.Concat(rows.Select(row=>string.Join(",",row.Select(v=>"\""+(v??"").Replace("\"","\"\"")+"\"")))))+"\r\n";
}
