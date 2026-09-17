using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class MainViewModel(ITaskRepository tasks,ITaskService taskService,ITaskSchedulingService scheduling,
    IAckLogRepository history,ISyncLogRepository syncLogs,ISyncService sync,SyncConfiguration configuration,
    IDeviceIdentityService identity,IAlarmPresenter presenter,IUserDialogs dialogs,ILunarCalendarRepository lunar,AdminViewModel admin,PreferencesViewModel preferences,PomodoroViewModel pomodoro,IAlarmHeartbeat heartbeat):ObservableObject
{
    public PomodoroViewModel Pomodoro=>pomodoro;
    [ObservableProperty] private int historyTabIndex;
    public AdminViewModel Admin=>admin;
    public PreferencesViewModel Preferences=>preferences;
    partial void OnPageIndexChanged(int value)
    {
        if(value==5&&!Admin.Session.IsAuthenticated){PageIndex=0;return;}
        NavigationIndex=value;
        foreach(var item in NavigationItems)item.IsSelected=item.PageIndex==value;
    }
    [ObservableProperty] private int navigationIndex;
    partial void OnNavigationIndexChanged(int value)
    {
        if(value<0)return;
        if(value==5&&!Admin.Session.IsAuthenticated)
        {
            Admin.LoginCommand.Execute(null);
            if(!Admin.Session.IsAuthenticated)NavigationIndex=PageIndex;
            return;
        }
        PageIndex=value;
    }
    public string Title=>"Cloud Alarm Overlay";
    public string[] Pages {get;}=["首頁","我的任務","歷史紀錄","番茄鐘","設定","管理者專區"];
    public NavigationItem TaskBuilderNavigationItem {get;} = new("任務產生器", "\uE943", -1);
    public NavigationItem[] NavigationItems {get;} =
    [
        new("首頁", "\uE80F", 0), new("我的任務", "\uE8FD", 1),
        new("歷史紀錄", "\uE81C", 2), new("番茄鐘", "\uE916", 3),
        new("設定", "\uE713", 4),
        new("管理者專區", "\uE72E", 5)
    ];
        [ObservableProperty] private string solarDate="";
    [ObservableProperty] private string lunarToday="";
    [ObservableProperty] private string currentClock="";
    [ObservableProperty] private string currentSolarTerm="-";
    private DateOnly? calendarDate;
    public void UpdateClock(DateTime now)
    {
        SolarDate=now.ToString("yyyy/MM/dd ddd",CultureInfo.GetCultureInfo("zh-TW"));
        CurrentClock=now.ToString("HH:mm:ss");
        LocalScheduleHealth=heartbeat.GetStatus(now);
        if(calendarDate!=DateOnly.FromDateTime(now))_ = RefreshCalendarAsync(now);
    }
    public async Task RefreshCalendarAsync(DateTime now)
    {
        var date=DateOnly.FromDateTime(now);calendarDate=date;
        SolarDate=now.ToString("yyyy/MM/dd ddd",CultureInfo.GetCultureInfo("zh-TW"));CurrentClock=now.ToString("HH:mm:ss");
        LunarToday=LocalLunarDate(now);CurrentSolarTerm="-";
        try
        {
            var entry=await lunar.GetByDateAsync(date);
            if(calendarDate!=date)return;
            if(!string.IsNullOrWhiteSpace(entry?.LunarDate))LunarToday=entry.LunarDate;
            if(calendarDate==date)CurrentSolarTerm=string.IsNullOrWhiteSpace(entry?.SolarTerm)?"-":entry.SolarTerm;
        }
        catch{if(calendarDate==date)CurrentSolarTerm="節氣資料暫不可用";}
    }
    public static string LocalLunarDate(DateTime now)
    {
        var calendar=new ChineseLunisolarCalendar();
        if(now<calendar.MinSupportedDateTime||now>calendar.MaxSupportedDateTime)return "日期超出支援範圍";
        var year=calendar.GetYear(now);var month=calendar.GetMonth(now);var leap=calendar.GetLeapMonth(year);
        var isLeap=month==leap;
        if(leap>0&&month>=leap)month--;
        string[] months=["正","二","三","四","五","六","七","八","九","十","冬","臘"];
        string[] digits=["一","二","三","四","五","六","七","八","九"];
        var d=calendar.GetDayOfMonth(now);
        var day=d==10?"初十":d==20?"二十":d==30?"三十":(d<10?"初":d<20?"十":"廿")+digits[(d-1)%10];
        return (isLeap?"閏":"")+months[month-1]+"月"+day;
    }
    public ObservableCollection<TaskRow> Tasks {get;}=[];
    public List<TaskRow> SelectedTasks {get;}=[];
    private bool rebuildingTasks;
    public event Action? TaskSelectionRestored;
    public bool HasTaskSelection=>SelectedTasks.Count>0;
    public bool CanEditSelectedTask=>SelectedTasks.Count==1&&SelectedTasks[0].IsLocal;
    public bool CanCopySelectedTask=>SelectedTasks.Count==1;
    public string SelectionHint=>!HasTaskSelection?"勾選任務以進行操作；雲端任務可複製成本機。":
        SelectedTasks.All(t=>!t.IsLocal)?"🔒 已選取雲端唯讀任務：不可編輯、啟停或刪除；可複製成本機後修改。":
        SelectedTasks.Any(t=>!t.IsLocal)?"🔒 混合選取：批次啟用、停用及刪除只處理本機任務，雲端設定保持不變。":
        SelectedTasks.Count>1?"已選取多筆本機任務，請使用批次操作；編輯請只選一筆。":"已選取本機任務，可編輯、複製、啟停或刪除。";
    public string SelectionSummary=>$"已選取 {SelectedTasks.Count} 筆（本機 {SelectedTasks.Count(t=>t.IsLocal)} 筆、雲端 {SelectedTasks.Count(t=>!t.IsLocal)} 筆）";
    public void SetTaskSelection(IEnumerable<TaskRow> rows)
    {
        if(rebuildingTasks)return;
        var snapshot=rows.ToArray();
        SelectedTasks.Clear();SelectedTasks.AddRange(snapshot);OnPropertyChanged(nameof(SelectionSummary));OnPropertyChanged(nameof(HasTaskSelection));
        OnPropertyChanged(nameof(CanEditSelectedTask));OnPropertyChanged(nameof(CanCopySelectedTask));OnPropertyChanged(nameof(SelectionHint));
        EditTaskCommand.NotifyCanExecuteChanged();CopyTaskCommand.NotifyCanExecuteChanged();
        DeleteTaskCommand.NotifyCanExecuteChanged();ToggleTaskCommand.NotifyCanExecuteChanged();BatchTasksCommand.NotifyCanExecuteChanged();
    }
    private bool CanBatchTasks(string operation)=>operation=="copy"?HasTaskSelection:SelectedTasks.Any(t=>t.IsLocal);
    [RelayCommand(CanExecute=nameof(CanBatchTasks))] private async Task BatchTasksAsync(string operation)
    {
        var selected=SelectedTasks.ToArray();
        var eligible=selected.Where(t=>operation=="copy"||t.IsLocal).ToArray();
        if(eligible.Length==0){Status="請選取任務；雲端任務只可複製成本機。";return;}
        var action=operation switch {"copy"=>"複製成本機（先停用，編輯確認後再啟用）","delete"=>"刪除","enable"=>"啟用",_=>"停用"};
        if(!dialogs.Confirm($"確定{action} {eligible.Length} 筆任務？"+(operation=="copy"?"":"雲端任務將略過，既有簽收紀錄保留。")))return;
        int completed=0,failed=0;string? error=null;
        foreach(var row in eligible)
        {
            try
            {
                if(operation=="delete")await taskService.DeleteLocalAsync(row.Task.Id);
                else if(operation=="copy")await taskService.SaveLocalAsync(row.Task with {
                    Id=Guid.NewGuid().ToString("N"),ExternalId=null,Source=TaskSources.Local,
                    TargetDeviceOrName=null,ExcludeDeviceOrName=null,IsTriggered=false,Enabled=false,
                    CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now});
                else await taskService.SaveLocalAsync(row.Task with {Enabled=operation=="enable"});
                completed++;
            }
            catch(Exception ex){failed++;error=ex.Message;}
        }
        await RefreshAsync();
        Status=$"批次作業：完成 {completed} 筆、略過 {selected.Length-eligible.Length} 筆雲端、失敗 {failed} 筆。"+(error is null?"":$" {error}");
    }
    public ObservableCollection<TaskRow> Upcoming {get;}=[];
    public ObservableCollection<HistoryRow> History {get;}=[];
    public ObservableCollection<SyncLogEntry> SyncLogs {get;}=[];
    private IReadOnlyList<AlarmTask> allTasks=[];
    private IReadOnlyList<AcknowledgementLog> allHistory=[];
    [ObservableProperty] private int pageIndex;
    [ObservableProperty] private TaskRow? selectedTask;
    [ObservableProperty] private string search="";
    [ObservableProperty] private string historySearch="";
    [ObservableProperty] private string sourceFilter="全部來源";
    public string[] Sources {get;}=["全部來源",TaskSources.Local,TaskSources.SheetA,TaskSources.SheetB];
    [ObservableProperty] private string status="準備就緒";
    [ObservableProperty] private string syncMessage="";
    [ObservableProperty] private string deviceLabel="尚未設定裝置";
    [ObservableProperty] private string nextTitle="目前沒有即將到來的提醒";
    [ObservableProperty] private string nextTime="新增本機任務，或前往設定連接公開試算表。";
    [ObservableProperty] private string nextClock="—";
    [ObservableProperty] private string countdown="—";
    [ObservableProperty] private string countdownLabel="—";
    [ObservableProperty] private double remainingHours;
    [ObservableProperty] private TaskRow? nextReminder;
    [ObservableProperty] private string nextDate="尚未排定";
    [ObservableProperty] private string nextLunar="";
    [ObservableProperty] private string nextDescription="";
    [ObservableProperty] private int pendingCount;
    
    private DateTime? nextReminderAt;
    public void UpdateCountdown()
    {
        UpdateClock(DateTime.Now);
        if(nextReminderAt is not {} at){Countdown="—";CountdownLabel="—";RemainingHours=0;return;}
        var remaining=at-DateTime.Now;
        if(remaining<TimeSpan.Zero)remaining=TimeSpan.Zero;
        Countdown=$"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        RemainingHours=remaining.TotalHours;
        CountdownLabel=remaining.TotalDays>=1?$"{(int)remaining.TotalDays}天{remaining.Hours}時":
            remaining.TotalHours>=1?$"{remaining.Hours}時{remaining.Minutes}分":
            remaining.TotalMinutes>=1?$"{remaining.Minutes}分{remaining.Seconds}秒":$"{remaining.Seconds}秒";
    }
    [ObservableProperty] private int todayCount;
    [ObservableProperty] private int acknowledgedCount;
    [ObservableProperty] private int overdueCount;
    [ObservableProperty] private string sheetAStatus="尚未設定";
    [ObservableProperty] private string sheetBStatus="尚未設定";
    [ObservableProperty] private string calendarWarning="";
    public bool HasCalendarWarning=>CalendarWarning.Length>0;
    [ObservableProperty] private string sheetAId="";
    [ObservableProperty] private string tasksAGid="";
    [ObservableProperty] private string holidaysGid="";
    [ObservableProperty] private string employeesGid="";
    [ObservableProperty] private string lunarGid="";
    [ObservableProperty] private string sheetBId="";
    [ObservableProperty] private string tasksBGid="";
    [ObservableProperty] private int intervalSeconds=45;
    public bool IsTasksEmpty=>Tasks.Count==0;
    public bool IsUpcomingEmpty=>Upcoming.Count==0;
    public bool IsHistoryEmpty=>History.Count==0;
    partial void OnSearchChanged(string value)=>FilterTasks();
    partial void OnSourceFilterChanged(string value)=>FilterTasks();
    partial void OnHistorySearchChanged(string value)=>FilterHistory();
    public async Task InitializeAsync()
    {
        await Preferences.LoadAsync();
        await Pomodoro.LoadAsync();
        var options=await configuration.LoadAsync();
        SheetAId=options.SheetAId;TasksAGid=options.TasksAGid;HolidaysGid=options.HolidaysGid;EmployeesGid=options.EmployeesGid;
        LunarGid=options.LunarGid;SheetBId=options.SheetBId;TasksBGid=options.TasksBGid;IntervalSeconds=options.IntervalSeconds;
        var device=await identity.GetLocalAsync();
        DeviceLabel=device is null?"尚未設定裝置":$"{device.DisplayName} · {device.DeviceId}";
        await RefreshCalendarAsync(DateTime.Now);
        await RefreshAsync();
    }
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await RefreshHealthAsync();
            await RefreshCalendarAsync(DateTime.Now);
            allTasks=await tasks.GetAllAsync();FilterTasks();
            CalendarWarning=allTasks.Any(t=>t.Enabled&&t.Recurrence.StartsWith("LunarDay:",StringComparison.Ordinal))
                &&await lunar.GetByDateAsync(DateOnly.FromDateTime(DateTime.Today)) is null
                ?"缺少今日農曆對照：農曆任務今天不會觸發，請 IT 更新 LunarCalendar 分頁。":"";
            OnPropertyChanged(nameof(HasCalendarWarning));
            allHistory=await history.GetRangeAsync(DateTime.MinValue,DateTime.MaxValue);FilterHistory(false);
            var now=DateTime.Now;
            var upcoming=new List<TaskRow>();
            foreach(var task in allTasks)
            {
                if(await scheduling.GetNextOccurrenceAsync(task,now) is {} next)upcoming.Add(new TaskRow(task,next));
            }
            Upcoming.Clear();foreach(var row in upcoming.OrderBy(t=>t.NextAt).Take(8))Upcoming.Add(row);
            NextTitle=Upcoming.FirstOrDefault()?.Title??"目前沒有即將到來的提醒";
            nextReminderAt=Upcoming.FirstOrDefault()?.NextAt;
            NextClock=nextReminderAt?.ToString("HH:mm")??"—";
            NextReminder=Upcoming.FirstOrDefault();
            NextDate=nextReminderAt?.ToString("yyyy/MM/dd（ddd）")??"尚未排定";
            NextDescription=NextReminder is null?"新增本機任務，安排下一個提醒。":
                string.IsNullOrWhiteSpace(NextReminder.Task.Description)?"目前沒有補充說明。":NextReminder.Task.Description;
            var nextLunarEntry=nextReminderAt is {} date?await lunar.GetByDateAsync(DateOnly.FromDateTime(date)):null;
            NextLunar=string.IsNullOrWhiteSpace(nextLunarEntry?.LunarDate)?"":"農曆 "+nextLunarEntry.LunarDate;
            UpdateCountdown();
            NextTime=Upcoming.FirstOrDefault() is {} first?$"{first.NextAt:MM/dd HH:mm:ss}  ·  {CloudAlarmOverlay.App.Styles.AlarmLevelLabelConverter.Label(first.Level)}  ·  {first.Source}":"新增本機任務，或前往設定連接公開試算表。";
            var todayHistory=allHistory.Where(h=>(h.ScheduledAt??h.TriggeredAt).Date==now.Date).ToArray();
            TodayCount=upcoming.Count(t=>t.NextAt?.Date==now.Date)+todayHistory.Length;
            AcknowledgedCount=todayHistory.Count(h=>h.AcknowledgedAt is not null);
            PendingCount=TodayCount-AcknowledgedCount;
            OverdueCount=allHistory.Count(h=>h.ScheduledAt?.Date==now.Date&&h.Result is "NotLaunched" or "Overdue_Unacked");
            var logs=await syncLogs.GetRangeAsync(now.AddDays(-7),now);
            SyncLogs.Clear();foreach(var log in logs.Take(30))SyncLogs.Add(log);
            OnPropertyChanged(nameof(IsUpcomingEmpty));
        }
        catch(Exception ex){Status=ex.Message;}
    }
    [ObservableProperty] private HealthStatus localScheduleHealth=new("等待啟動","尚未檢查","Pending");
    [ObservableProperty] private HealthStatus sheetAHealth=new("未設定","尚未檢查","Pending");
    [ObservableProperty] private HealthStatus sheetBHealth=new("未設定","尚未檢查","Pending");
    public async Task RefreshHealthAsync()
    {
        var now=DateTime.Now;
        LocalScheduleHealth=heartbeat.GetStatus(now);
        try
        {
            var options=await configuration.LoadAsync();
            var device=await identity.GetLocalAsync();
            var states=await syncLogs.GetStatesAsync();
            SheetAHealth=SyncHealth.ForSheet("SheetA",options,device,states,now);
            SheetBHealth=SyncHealth.ForSheet("SheetB",options,device,states,now);
        }
        catch(Exception ex){SheetAHealth=SheetBHealth=new("異常","無法讀取同步狀態："+ex.Message,"Error");}
        SheetAStatus=SheetAHealth.Label+" · "+SheetAHealth.Detail;
        SheetBStatus=SheetBHealth.Label+" · "+SheetBHealth.Detail;
    }
    private void FilterTasks()
    {
        var selectedIds=SelectedTasks.Select(t=>t.Task.Id).ToHashSet();
        rebuildingTasks=true;
        try {
        Tasks.Clear();
        foreach(var task in allTasks.Where(t=>(SourceFilter=="全部來源"||t.Source==SourceFilter)&&(t.Title.Contains(Search,StringComparison.OrdinalIgnoreCase)||t.Description?.Contains(Search,StringComparison.OrdinalIgnoreCase)==true)))
            Tasks.Add(new TaskRow(task));
        } finally {rebuildingTasks=false;}
        SetTaskSelection(Tasks.Where(t=>selectedIds.Contains(t.Task.Id)).ToArray());
        SelectedTask=SelectedTasks.FirstOrDefault();
        TaskSelectionRestored?.Invoke();
        OnPropertyChanged(nameof(IsTasksEmpty));
    }
    [RelayCommand] private void OpenAdmin(string tab)
    {
        if(!Admin.Session.IsAuthenticated){Admin.LoginCommand.Execute(null);return;}
        Admin.SelectedTab=int.Parse(tab);PageIndex=5;
    }
    [RelayCommand] private void OpenTasks()=>PageIndex=1;
    [RelayCommand] private void NewTask()=>dialogs.Edit(null,false);
    [RelayCommand(CanExecute=nameof(CanEditSelectedTask))] private void EditTask(){if(SelectedTask is {IsLocal:true} row)dialogs.Edit(row.Task,false);else Status="請選取本機任務；雲端任務為唯讀。";}
    [RelayCommand(CanExecute=nameof(CanCopySelectedTask))] private void CopyTask(){if(SelectedTask is {} row)dialogs.Edit(row.Task,true);else Status="請先選取任務。";}
    [RelayCommand(CanExecute=nameof(CanEditSelectedTask))] private async Task DeleteTaskAsync()
    {
        try
        {
            if(SelectedTask is not {IsLocal:true} row){Status="請選取可刪除的本機任務。";return;}
            if(dialogs.Confirm($"確定刪除「{row.Title}」？既有簽收紀錄會保留。")){await taskService.DeleteLocalAsync(row.Task.Id);await RefreshAsync();}
        }
        catch(Exception ex){Status=ex.Message;}
    }
    private bool CanToggleRow(TaskRow? row)=>row?.IsLocal==true;
    [RelayCommand(CanExecute=nameof(CanToggleRow))] private async Task ToggleRowAsync(TaskRow row)
    {
        if(!row.IsLocal)return;
        try
        {
            await taskService.SaveLocalAsync(row.Task with {Enabled=!row.Task.Enabled});
            await RefreshAsync();
            Status=$"「{row.Title}」已{(row.Task.Enabled?"停用":"啟用")}。";
        }
        catch(Exception ex){Status="切換失敗："+ex.Message;}
    }
    [RelayCommand(CanExecute=nameof(CanEditSelectedTask))] private async Task ToggleTaskAsync()
    {
        try
        {
            if(SelectedTask is not {IsLocal:true} row){Status="雲端任務的啟用狀態為唯讀。";return;}
            await taskService.SaveLocalAsync(row.Task with{Enabled=!row.Task.Enabled});await RefreshAsync();
        }
        catch(Exception ex){Status=ex.Message;}
    }
    [RelayCommand] private async Task SyncAsync()
    {
        try
        {
            var saved=await configuration.LoadAsync();
            if(CurrentOptions()!=saved)
            {
                SetSyncMessage("畫面上的同步設定尚未儲存；請按「儲存並同步」後再試。");
                return;
            }
            SetSyncMessage("正在同步公開 CSV；若背景同步正在執行，將接續執行…");
            var result=await sync.SyncAsync();
            if(result.SkippedReason is not null){SetSyncMessage(result.SkippedReason);return;}
            await RefreshAsync();
            if(Admin.IsAuthenticated)await Admin.RefreshLogsCommand.ExecuteAsync(null);
            var failed=result.Entries.Where(e=>e.Status=="失敗").ToArray();
            var succeeded=result.Entries.Count-failed.Length;
            var summary=$"同步完成：{succeeded} 個分頁成功、{failed.Length} 個失敗；任務下載 {result.DownloadedTaskCount} 筆，本機符合 {result.IncludedTaskCount} 筆。";
            if(failed.Length>0)summary+=$" {failed[0].Source}：{failed[0].Message}";
            else if(result.DownloadedTaskCount>0&&result.IncludedTaskCount==0)
                summary+=" 若預期有任務，請檢查本機裝置 ID 與 Sheet 任務對象欄位。";
            SetSyncMessage(summary);
        }
        catch(Exception ex){SetSyncMessage("同步失敗："+ex.Message);}
    }
    private void SetSyncMessage(string message){Status=message;SyncMessage=message;Admin.Message=message;}
    private SyncOptions CurrentOptions()=>new(){SheetAId=SheetAId.Trim(),TasksAGid=TasksAGid.Trim(),HolidaysGid=HolidaysGid.Trim(),EmployeesGid=EmployeesGid.Trim(),LunarGid=LunarGid.Trim(),SheetBId=SheetBId.Trim(),TasksBGid=TasksBGid.Trim(),IntervalSeconds=IntervalSeconds};
    [RelayCommand] private Task TestSheetAAsync()=>Admin.TestConnectionAsync(CurrentOptions(),true);
    [RelayCommand] private Task TestSheetBAsync()=>Admin.TestConnectionAsync(CurrentOptions(),false);
    [RelayCommand] private async Task SaveSettingsAsync()
    {
        try
        {
            await configuration.SaveAsync(new SyncOptions{SheetAId=SheetAId.Trim(),TasksAGid=TasksAGid.Trim(),HolidaysGid=HolidaysGid.Trim(),EmployeesGid=EmployeesGid.Trim(),
                LunarGid=LunarGid.Trim(),SheetBId=SheetBId.Trim(),TasksBGid=TasksBGid.Trim(),IntervalSeconds=IntervalSeconds});
            SetSyncMessage("同步設定已儲存，準備同步…");await SyncAsync();
        }
        catch(Exception ex){SetSyncMessage("無法儲存同步設定："+ex.Message);}
    }
    [RelayCommand] private async Task PreviewAsync(string level)
    {
        try
        {
            await presenter.ShowAsync("preview",new AlarmTask{Id="preview",Title="通知效果預覽",Description="這是測試通知。完成確認即可關閉，不會建立簽收紀錄。",
                Level=level,RequireAcknowledgement=level!=AlarmLevels.Low,ScheduledAt=DateTime.Now,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now},DateTime.Now,true);
        }
        catch(Exception ex){Status=ex.Message;}
    }
}
public sealed record TaskRow(AlarmTask Task,DateTime? NextAt=null)
{
    public string UpcomingState=>"等待觸發";
    public string Title=>Task.Title;
    public string Source=>Task.Source;
    public string Access=>IsLocal?"本機":"🔒 "+Source;
    public string Level=>Task.Level;
    public string Time=>Task.ScheduledAt.ToString("MM/dd HH:mm:ss");
    public string NextTime=>NextAt?.ToString("MM/dd HH:mm")??"—";
    public string State=>Task.Enabled?"已啟用":"已停用";
    public string StateOrigin=>IsLocal?"本機可調整":$"依 {Source} 設定";
    public string Recurrence=>CloudAlarmOverlay.Core.Recurrence.RecurrenceRule.Describe(Task.Recurrence);
    public bool IsLocal=>Task.Source==TaskSources.Local;
}
public sealed record HistoryRow(AcknowledgementLog Entry)
{
    public string Title=>Entry.TaskName??Entry.TaskId;
    public string Scheduled=>Entry.ScheduledAt?.ToString("yyyy/MM/dd HH:mm:ss")??"—";
    public string Triggered=>Entry.Result=="NotLaunched"?"—":Entry.TriggeredAt.ToString("yyyy/MM/dd HH:mm:ss");
    public string Acknowledged=>Entry.AcknowledgedAt?.ToString("yyyy/MM/dd HH:mm:ss")??"—";
    public string Source=>Entry.Source??"—";
    public string Duration=>Entry.DurationSeconds is {} seconds?$"{seconds/60} 分 {seconds%60} 秒":"—";
    public string Result=>Entry.Result switch{"Pending"=>"等待確認","Acknowledged"=>"準時簽收","Overdue_Acknowledged"=>"逾期簽收","Overdue_Unacked"=>"逾期未簽收","NotLaunched"=>"未開機",var r=>r};
}
