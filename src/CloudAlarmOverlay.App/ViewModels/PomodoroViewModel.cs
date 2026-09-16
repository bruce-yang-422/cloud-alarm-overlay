using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.App.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class PomodoroViewModel:ObservableObject
{
    private readonly IPomodoroService timer;
    private readonly IPomodoroRepository repository;
    private readonly IUserDialogs dialogs;
    private readonly ISoundService sound;
    private bool loading;
    private bool refreshing;
    [ObservableProperty] private bool isPreviewing;
    [ObservableProperty] private string previewStatus="";
    public string PreviewButtonLabel=>IsPreviewing?"◉ 試聽中":"▷ 試聽";
    partial void OnIsPreviewingChanged(bool value)=>OnPropertyChanged(nameof(PreviewButtonLabel));
    private readonly SemaphoreSlim refreshGate=new(1,1);
    private DateTime lastRefresh;
    public PomodoroViewModel(IPomodoroService timer,IPomodoroRepository repository,IUserDialogs dialogs,ISoundService sound)
    {
        this.timer=timer;this.repository=repository;this.dialogs=dialogs;this.sound=sound;
        timer.Changed+=()=>{if(Application.Current is {} app)app.Dispatcher.InvokeAsync(Update);};
    }
    public async Task LoadAsync()
    {
        await timer.InitializeAsync();
        var available=Sounds;
        if(timer.State.Status=="Idle"&&available.Count>0&&!available.Contains(timer.Options.SoundName))
            await timer.SaveOptionsAsync(timer.Options with{SoundName=available[0]});
        loading=true;
        var o=timer.Options;FocusMinutes=o.FocusMinutes;BreakMinutes=o.BreakMinutes;LongBreakMinutes=o.LongBreakMinutes;
        Interval=o.Interval;LongBreakEnabled=o.LongBreakEnabled;SoundEnabled=o.SoundEnabled;SoundName=o.SoundName;
        loading=false;Update();await RefreshAsync();
    }
    public string Clock=>$"{(int)Math.Ceiling(timer.State.Remaining.TotalSeconds)/60:00}:{(int)Math.Ceiling(timer.State.Remaining.TotalSeconds)%60:00}";
    public string Phase=>timer.State.Status=="Idle"?"待命中":timer.State.Phase=="Focus"?"專注中":timer.State.Phase=="Break"?"短休息":"長休息";
    public string StateLabel=>timer.State.Status=="Paused"?"已暫停":timer.State.Status=="AwaitingConfirmation"?"等待通知確認":Phase;
    public string RoundLabel=>$"第 {timer.State.Round} 輪";
    public string Started=>timer.State.StartedAt?.ToString("HH:mm")??"—";
    public string Next=>timer.NextPhase=="Focus"?$"專注 {FocusMinutes} 分鐘":
        timer.NextPhase=="LongBreak"?$"長休息 {LongBreakMinutes} 分鐘":$"短休息 {BreakMinutes} 分鐘";
    public string Dots=>string.Join("  ",Enumerable.Range(0,Interval).Select(i=>i<timer.State.Cycle?"●":i==timer.State.Cycle&&timer.State.Phase=="Focus"&&timer.State.Status!="Idle"?"◉":"○"));
    public string UntilLongBreak=>LongBreakEnabled?$"再完成 {Interval-timer.State.Cycle} 輪專注後長休息":"已停用長休息";
    public bool CanEdit=>timer.State.Status=="Idle";
    public bool IsActive=>!CanEdit;
    public bool CanToggle=>timer.State.Status!="AwaitingConfirmation";
    public string ToggleLabel=>timer.State.Status=="Running"?"暫停":timer.State.Status=="Paused"?"繼續":"開始專注";
    public string Encouragement=>Completed==0?"開始第一個番茄鐘，累積今天的專注時間！":Completed>=timer.Options.DailyGoal?"太棒了！已達成今日目標！":"很棒！持續專注，離目標更近了！";
    public string GoalLabel=>$"{Completed} / {timer.Options.DailyGoal}";
    public double ProgressHours=>Math.Min(24,24.0*Completed/timer.Options.DailyGoal);
    public bool HasProgress=>Completed>0;
    [ObservableProperty] private int completed;
    [ObservableProperty] private string average="平均 0.0 輪／日";
    [ObservableProperty] private string error="";
    [ObservableProperty] private int focusMinutes=25;
    [ObservableProperty] private int breakMinutes=5;
    [ObservableProperty] private int longBreakMinutes=15;
    [ObservableProperty] private int interval=4;
    [ObservableProperty] private bool longBreakEnabled=true;
    [ObservableProperty] private bool soundEnabled=true;
    [ObservableProperty] private string soundName=".Default";
    public IReadOnlyList<string> Sounds=>sound.GetAvailableSounds();
    public int[] FocusChoices=>Enumerable.Range(5,86).ToArray();
    public int[] BreakChoices=>Enumerable.Range(1,30).ToArray();
    public int[] LongChoices=>Enumerable.Range(5,56).ToArray();
    public int[] Intervals=>Enumerable.Range(2,7).ToArray();
    public ObservableCollection<PomodoroDay> Days {get;}=[];
    public ObservableCollection<PomodoroHistoryRow> History {get;}=[];
    private IReadOnlyList<PomodoroLogEntry> filtered=[];
    [ObservableProperty] private DateTime from=DateTime.Today.AddDays(-29);
    [ObservableProperty] private DateTime to=DateTime.Today;
    [ObservableProperty] private string phaseFilter="全部";
    [ObservableProperty] private string resultFilter="全部";
    [ObservableProperty] private int page=1;
    [ObservableProperty] private string historySummary="";
    public string[] Phases=>["全部","專注","短休息","長休息"];
    public string[] Results=>["全部","完成","中斷"];
    public string PageLabel=>$"第 {Page} / {Math.Max(1,(filtered.Count+14)/15)} 頁 · 共 {filtered.Count} 筆";
    public bool CanPrevious=>Page>1;
    public bool CanNext=>Page*15<filtered.Count;
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if(loading)return;
        if(e.PropertyName is nameof(FocusMinutes) or nameof(BreakMinutes) or nameof(LongBreakMinutes) or nameof(Interval) or nameof(LongBreakEnabled) or nameof(SoundEnabled) or nameof(SoundName))
            _=SaveAsync();
        if(e.PropertyName is nameof(From) or nameof(To) or nameof(PhaseFilter) or nameof(ResultFilter))_ = RefreshAsync();
    }
    private async Task SaveAsync()
    {
        try{await timer.SaveOptionsAsync(timer.Options with{FocusMinutes=FocusMinutes,BreakMinutes=BreakMinutes,LongBreakMinutes=LongBreakMinutes,Interval=Interval,LongBreakEnabled=LongBreakEnabled,SoundEnabled=SoundEnabled,SoundName=SoundName});Error="";}
        catch(Exception ex){Error=ex.Message;}
    }
    private void Update()
    {
        foreach(var p in new[]{nameof(Clock),nameof(Phase),nameof(StateLabel),nameof(RoundLabel),nameof(Started),nameof(Next),nameof(Dots),nameof(UntilLongBreak),nameof(CanEdit),nameof(IsActive),nameof(CanToggle),nameof(ToggleLabel)})
            OnPropertyChanged(p);
        if(!refreshing && DateTime.Now-lastRefresh>TimeSpan.FromSeconds(1))_=RefreshAsync();
    }
    private async Task Act(Func<Task> action){try{await action();Error="";await RefreshAsync();}catch(Exception ex){Error=ex.Message;}}
    [RelayCommand] private Task ToggleAsync()=>Act(()=>timer.State.Status=="Running"?timer.PauseAsync():timer.StartAsync());
    [RelayCommand] private Task SkipAsync()=>Act(()=>timer.SkipAsync());
    [RelayCommand] private async Task ResetAsync(){if(dialogs.Confirm("重設番茄鐘？目前尚未完成的階段將記為中斷。"))await Act(()=>timer.ResetAsync());}
    [RelayCommand] private async Task PreviewSoundAsync()
    {
        if(IsPreviewing)return;
        IsPreviewing=true;PreviewStatus="正在試聽…";
        try
        {
            if(string.IsNullOrWhiteSpace(SoundName))throw new InvalidOperationException("請先選擇要試聽的聲音。");
            await sound.PlayAsync(SoundName);
            PreviewStatus="已執行試聽；若無聲請檢查 Windows 靜音與音量。";
            await Task.Delay(1500);
            Error="";
        }
        catch(Exception ex){PreviewStatus="試聽失敗："+ex.Message;Error=ex.Message;}
        finally{IsPreviewing=false;}
    }
    [RelayCommand] public async Task RefreshAsync()
    {
        await refreshGate.WaitAsync();refreshing=true;
        try
        {
            var today=DateTime.Today;
            var recent=await repository.GetLogsAsync(today.AddDays(-6),today.AddDays(1));
            var done=recent.Where(x=>!x.IsActive&&x.Type=="Focus"&&x.Result=="Completed").ToArray();
            Completed=done.Count(x=>x.StartedAt.Date==today);
            var counts=Enumerable.Range(0,7).Select(i=>new{Date=today.AddDays(i-6),Count=done.Count(x=>x.StartedAt.Date==today.AddDays(i-6))}).ToArray();
            var max=Math.Max(1,counts.Max(x=>x.Count));Days.Clear();
            foreach(var d in counts)Days.Add(new(d.Date.ToString("ddd"),d.Date.ToString("M/d"),d.Count,80.0*d.Count/max,d.Date==today?"#EF725D":"#74B99B"));
            Average=$"平均 {done.Length/7.0:0.0} 輪／日";
            foreach(var p in new[]{nameof(Encouragement),nameof(GoalLabel),nameof(ProgressHours),nameof(HasProgress)})OnPropertyChanged(p);
            if(From.Date>To.Date){Error="開始日期不可晚於結束日期。";filtered=[];}
            else
            {
                var rows=await repository.GetLogsAsync(From.Date,To.Date.AddDays(1));
                filtered=rows.Where(x=>!x.IsActive&&(PhaseFilter=="全部"||PomodoroHistoryRow.PhaseName(x.Type)==PhaseFilter)&&(ResultFilter=="全部"||(x.Result=="Completed"?"完成":"中斷")==ResultFilter)).ToArray();
            }
            HistorySummary=$"完成專注 {filtered.Count(x=>x.Type=="Focus"&&x.Result=="Completed")} 輪 · 專注 {filtered.Where(x=>x.Type=="Focus"&&x.Result=="Completed").Sum(x=>x.PlannedMinutes)} 分鐘 · 中斷 {filtered.Count(x=>x.Result=="Interrupted")} 次";
            ShowPage();lastRefresh=DateTime.Now;
        }catch(Exception ex){Error=ex.Message;}finally{refreshing=false;refreshGate.Release();}
    }
    [RelayCommand] private async Task ClearFiltersAsync(){loading=true;From=DateTime.Today.AddDays(-29);To=DateTime.Today;PhaseFilter="全部";ResultFilter="全部";Page=1;loading=false;await RefreshAsync();}
    public int FilteredCompleted=>filtered.Count(x=>x.Type=="Focus"&&x.Result=="Completed");
    public int FilteredMinutes=>filtered.Where(x=>x.Type=="Focus"&&x.Result=="Completed").Sum(x=>x.PlannedMinutes);
    public int FilteredInterrupted=>filtered.Count(x=>x.Result=="Interrupted");
    public bool IsHistoryEmpty=>filtered.Count==0;
    private void ShowPage()
    {
        Page=Math.Clamp(Page,1,Math.Max(1,(filtered.Count+14)/15));History.Clear();
        foreach(var row in filtered.Skip((Page-1)*15).Take(15))History.Add(new(row));
        OnPropertyChanged(nameof(FilteredCompleted));OnPropertyChanged(nameof(FilteredMinutes));OnPropertyChanged(nameof(FilteredInterrupted));OnPropertyChanged(nameof(IsHistoryEmpty));OnPropertyChanged(nameof(PageLabel));OnPropertyChanged(nameof(CanPrevious));OnPropertyChanged(nameof(CanNext));
    }
    [RelayCommand] private void Previous(){Page--;ShowPage();}
    [RelayCommand] private void NextPage(){Page++;ShowPage();}
    [RelayCommand] private async Task ExportAsync(){await RefreshAsync();dialogs.ExportNamed(CsvExport.Build(["Date","Phase","StartedAt","EndedAt","PlannedMinutes","Status"],filtered.Select(x=>new string?[]{x.StartedAt.ToString("yyyy-MM-dd"),x.Type,CsvExport.Date(x.StartedAt),x.EndedAt is {} end?CsvExport.Date(end):"",x.PlannedMinutes.ToString(),x.Result}).ToArray()),$"PomodoroLog_{From:yyyyMMdd}_{To:yyyyMMdd}.csv");}
}
public sealed record PomodoroDay(string Weekday,string Date,int Count,double Height,string Color);
public sealed record PomodoroHistoryRow(PomodoroLogEntry Entry)
{
    public static string PhaseName(string type)=>type=="Focus"?"專注":type=="Break"?"短休息":"長休息";
    public string Phase=>PhaseName(Entry.Type);
    public string Started=>Entry.StartedAt.ToString("yyyy/MM/dd HH:mm:ss");
    public string Ended=>Entry.EndedAt?.ToString("yyyy/MM/dd HH:mm:ss")??"—";
    public string Planned=>Entry.PlannedMinutes>0?$"{Entry.PlannedMinutes} 分鐘":"—";
    public string Result=>Entry.Result=="Completed"?"完成":"中斷";
}
