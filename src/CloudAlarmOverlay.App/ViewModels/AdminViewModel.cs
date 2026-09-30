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
    ISystemEventStore events,IExitProtectionService exitProtection,IAutoStartService autoStart,IAuthenticationService authentication,CalendarDataViewModel calendarData):ObservableObject
{
    public CalendarDataViewModel CalendarData=>calendarData;
    public AdminSession Session=>session;
    public sealed record NavigationEntry(int Index,string Name,string Group,string Glyph);
    public NavigationEntry[] Navigation {get;}=[
        new(0,"總覽","","\uE80F"),new(1,"共用試算表","連線與資料","\uE8A5"),new(2,"農曆與假日","連線與資料","\uE787"),
        new(3,"使用者政策","政策與安全","\uE713"),new(4,"安全與存取","政策與安全","\uE72E"),
        new(5,"啟動與更新","維護與診斷","\uE777"),new(6,"備份與移轉","維護與診斷","\uE8B7"),
        new(7,"紀錄查詢","維護與診斷","\uE9D9"),new(8,"清理與重置","維護與診斷","\uE74D")];
    public int[] IntervalChoices {get;}=[30,35,40,45,50,55,60];
    public Func<int,bool>? CanLeavePage {get;set;}
    public event Action? ReloadStarting;
    public event Action? ReloadFinished;
    public bool ConfirmDiscardChanges()=>dialogs.ConfirmAdminAction("放棄尚未儲存的變更？","目前頁面的修改尚未儲存。放棄後會恢復已儲存設定。","放棄並離開");
    public bool IsCompanyMode=>!session.IsPersonal;
    public string AccessDescription=>session.IsPersonal?"個人使用 · 管理員功能直接開放，不需密碼。":"公司使用 · 管理員功能須登入，固定有效 10 分鐘。";
    [ObservableProperty] private string logRetentionDaysText="30";
    [RelayCommand] private async Task SaveLogRetentionAsync()
    {
        try
        {
            session.RequireAdmin();
            var days=LogRetentionPolicy.Validate(LogRetentionDaysText.Trim());
            await store.SaveAsync([new(){Key=LogRetentionPolicy.Key,Value=days.ToString(System.Globalization.CultureInfo.InvariantCulture)}]);
            LogRetentionDaysText=days.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Message=$"日誌保留天數已設為 {days} 天，下次自動檢查時套用（最晚一小時內）。";
        }
        catch(Exception ex) { Message=ex.Message; }
    }
    [RelayCommand] private async Task ExportCurrentSettingsAsync()
    {
        try
        {
            session.RequireAdmin();
            var json=await AdminSettingsJson.ExportAsync(settings);
            session.RequireAdmin();
            Message=dialogs.ExportSettingsJson(json,()=>session.RequireAdmin())
                ?"已匯出目前已儲存的設定。JSON 含連線資訊，請妥善保管。"
                :"已取消匯出設定。";
        }
        catch(UnauthorizedAccessException) { Message="請先登入管理者；登入逾時請重新驗證。"; }
        catch(Exception) { Message="無法匯出設定，請確認已儲存的設定有效且檔案可寫入。"; }
    }
    public async Task<bool> ImportSettingsJsonAsync()
    {
        session.RequireAdmin();
        var text=await dialogs.OpenSettingsJsonAsync();
        if(text is null)return false;
        var imported=AdminSettingsJson.Parse(text);
        session.RequireAdmin();
        var labels=new Dictionary<string,string>{["SyncOptions"]="共用試算表來源與同步間隔",["UpdateManifestUrl"]="程式更新網址",["SyncLinksLocked"]="來源鎖定",["AllowUrgentSnooze"]="緊急提醒延後政策",["FlashMilliseconds"]="閃爍間隔與鎖定",["QuietPeriods"]="靜音時段與鎖定",["WeatherDefaultLocation"]="公司預設天氣地點",[LogRetentionPolicy.Key]="執行日誌保留天數"};
        var preview=string.Join("\n",imported.Select(item=>"• "+labels.GetValueOrDefault(item.Key,item.Key)));
        if(!dialogs.ConfirmAdminAction("預覽管理設定匯入","將套用下列項目：\n\n"+preview+"\n\n未列出的設定維持原值。確認後套用到此電腦。","套用設定"))return false;
        session.RequireAdmin();
        await store.SaveAsync(imported);
        return true;
    }
    [RelayCommand] private void ExportSettingsTemplate()
    {
        try { session.RequireAdmin(); dialogs.ExportSettingsTemplate(); }
        catch(Exception ex) { Message=ex.Message; }
    }
    public PreferencesViewModel Preferences=>preferences;
    [ObservableProperty] private bool isAuthenticated;
    [ObservableProperty] private int selectedTab=1;
    [ObservableProperty] private int selectedLogKind;
    [ObservableProperty] private string message="";
    public string WorkspaceMessage=>!ShowsLogFilter&&Message.StartsWith("已載入 ",StringComparison.Ordinal)?"":Message;
    partial void OnMessageChanged(string value)=>OnPropertyChanged(nameof(WorkspaceMessage));
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
    [ObservableProperty] private bool allowUrgentSnooze=true;
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
    public bool ShowsLogFilter=>SelectedTab==7;
    private bool revertingNavigation;
    partial void OnSelectedTabChanged(int oldValue,int newValue)
    {
        if(!revertingNavigation && (newValue<0||newValue>=Navigation.Length||CanLeavePage?.Invoke(oldValue)==false))
        {
            revertingNavigation=true;SelectedTab=oldValue;revertingNavigation=false;
        }
        OnPropertyChanged(nameof(ShowsLogFilter));
        OnPropertyChanged(nameof(WorkspaceMessage));
    }
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
        OnPropertyChanged(nameof(IsCompanyMode));OnPropertyChanged(nameof(AccessDescription));
        if(!IsAuthenticated)
        {
            Audit.Clear();Logs.Clear();Events.Clear();Message="已登出管理者模式。";
            if(old is not null)await auditService.RecordAsync(new AuditLogEntry{UserId=old,Action="管理者登出／登入期限屆滿",CreatedAt=DateTime.Now});
            return;
        }
        await LoadAsync();
    }
    public async Task LoadAsync()
    {
        ReloadStarting?.Invoke();
        try
        {
            session.RequireAdmin();
            await preferences.LoadAsync();
            AllowUrgentSnooze=(await settings.GetAsync("AllowUrgentSnooze"))?.Value!="false";
            LockFlash=preferences.FlashLocked;LockQuiet=preferences.QuietLocked;FlashMilliseconds=preferences.FlashMilliseconds;
            LinksLocked=(await settings.GetAsync("SyncLinksLocked"))?.Value=="true";
            await RefreshExitPasswordModeAsync();
            ExitPasswordRequired=await exitProtection.IsRequiredAsync();
            AutoStartEnabled=autoStart.IsEnabled();
            LogRetentionDaysText=LogRetentionPolicy.Read((await settings.GetAsync(LogRetentionPolicy.Key))?.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(LinksEditable));
            await RefreshLogsAsync();
        }
        catch(Exception ex){Message=ex.Message;}
        finally{ReloadFinished?.Invoke();}
    }
    [RelayCommand] private void Login()=>LoginRequested?.Invoke();
    [RelayCommand] private void Logout()=>session.SignOut();
    public Task ChangeCredentialsAsync(string currentPassword,string newPassword)
        =>authentication.ChangeCredentialsAsync(currentPassword,AdminUsername,newPassword);
    private async Task RefreshExitPasswordModeAsync()
        =>ExitPasswordModeLabel=await exitProtection.GetModeAsync()=="Dedicated"?"目前方式：專用結束密碼":session.IsPersonal?"尚未設定專用結束密碼":"目前方式：管理員帳號與密碼";
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
        try {await SavePoliciesAsync();}
        catch(Exception ex){Message=ex.Message;}
    }
    public async Task SavePoliciesAsync(bool includeWeather=false)
    {
            session.RequireAdmin();
            if(FlashMilliseconds is <200 or >5000)throw new ArgumentException("閃爍間隔須為 200–5000 毫秒。");
            List<Setting> values=[
                new Setting{Key="AllowUrgentSnooze",Value=AllowUrgentSnooze?"true":"false",Locked=true},
                new Setting{Key="FlashMilliseconds",Value=FlashMilliseconds.ToString(),Locked=LockFlash},
                new Setting{Key="QuietPeriods",Value=(await settings.GetAsync("QuietPeriods"))?.Value??"[]",Locked=LockQuiet}];
            if(includeWeather)
            {
                var location=preferences.Weather?.SelectedDistrict?.Location??throw new ArgumentException("請選擇公司預設天氣的縣市及鄉鎮市區。");
                values.Add(new(){Key="WeatherDefaultLocation",Value=JsonSerializer.Serialize(location),Locked=true});
            }
            session.RequireAdmin();await store.SaveAsync(values);
            await preferences.LoadAsync();await RefreshLogsAsync();Message="本機鎖定設定已儲存。";
    }
    public async Task SaveWorkspaceSectionAsync(int page)
    {
        session.RequireAdmin();
        switch(page)
        {
            case 4:
                await exitProtection.SetRequiredAsync(ExitPasswordRequired);Message="結束程式保護設定已儲存。";break;
            case 5:
                var url=preferences.Maintenance.UpdateUrl.Trim();
                if(url.Length>0)UpdateCheckService.ValidateUrl(url);
                // Validate both inputs before changing the Windows startup setting.
                session.RequireAdmin();var previousAutoStart=autoStart.IsEnabled();
                autoStart.SetEnabled(AutoStartEnabled);
                try{await store.SaveAsync([new(){Key="UpdateManifestUrl",Value=url}]);}
                catch{autoStart.SetEnabled(previousAutoStart);throw;}
                preferences.Maintenance.UpdateUrl=url;Message="啟動與更新設定已儲存。";break;
            case 8:
                var days=LogRetentionPolicy.Validate(LogRetentionDaysText.Trim());
                await store.SaveAsync([new(){Key=LogRetentionPolicy.Key,Value=days.ToString(System.Globalization.CultureInfo.InvariantCulture)}]);
                LogRetentionDaysText=days.ToString(System.Globalization.CultureInfo.InvariantCulture);Message="日誌保留設定已儲存，下次自動清理時套用。";break;
            default:throw new ArgumentException("此頁沒有可儲存的設定。");
        }
    }
    [RelayCommand] private async Task ToggleLinksAsync()
    {
        try
        {
            var next=!LinksLocked;
            await store.SaveAsync([
                new Setting{Key="SyncLinksLocked",Value=next?"true":"false"}]);
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
    [RelayCommand] private async Task PruneSyncLogsAsync()
    {
        try
        {
            session.RequireAdmin();
            if(!dialogs.ConfirmAdminAction("清理舊成功紀錄？","只清理 30 天前的舊版成功輪詢紀錄。舊版未記錄異動明細，建議先匯出備查。失敗紀錄、新版異動事件及任務確認紀錄會保留。","清理舊紀錄",true))return;
            session.RequireAdmin();
            var count=await syncLogs.PruneLegacySuccessAsync(DateTime.Now.AddDays(-30));
            await RefreshLogsAsync();
            Message=$"已清理 {count} 筆舊版成功輪詢紀錄。";
        }
        catch(Exception ex){Message=ex.Message;}
    }
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
                _=>CsvExport.Build(["Time","Source","Status","Message","RecordCount","LastSeenAt","RepeatCount","EventKind"],Logs.Select(r=>new[]{CsvExport.Date(r.Time),r.Source,r.Status,r.Message,r.RecordCount?.ToString(),r.LastSeenAt is {} at?CsvExport.Date(at):"",r.RepeatCount.ToString(),r.EventKind}))};
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
