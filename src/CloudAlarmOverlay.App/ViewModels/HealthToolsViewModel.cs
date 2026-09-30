using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.ViewModels;

public partial class HealthToolsViewModel : ObservableObject
{
    private readonly HealthToolsService health;
    private readonly IPomodoroService pomodoro;
    private HealthToolsState? rendered;
    private DateTime renderedDate;
    public HealthToolsViewModel(HealthToolsService health, IPomodoroService pomodoro)
    {
        this.health = health; this.pomodoro = pomodoro;
        Cards = HealthTools.Kinds.Select(k => new HealthToolCard(health, k, EditTool)).ToArray();
    }
    public HealthToolCard[] Cards { get; }
    public string[] PageNames { get; } = ["首頁", "喝水", "久坐", "螢幕休息", "伸展"];
    public ObservableCollection<HealthTimelineRow> Timeline { get; } = [];
    public ObservableCollection<HealthWeekDay> Week { get; } = [];
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool settingsOpen;
    [ObservableProperty] private int selectedTabIndex;
    [ObservableProperty] private string coordinationMode = "延後";
    public string[] CoordinationModes { get; } = ["自動對齊", "覆蓋", "延後"];
    [ObservableProperty] private string notificationPosition = "右邊";
    public string[] NotificationPositions { get; } = ["左邊", "置中", "右邊"];
    [ObservableProperty] private bool showAllHistory;
    public int EnabledCount => health.Snapshot.Tools.Count(t => t.Options.Enabled);
    public bool HasEnabledTools => EnabledCount > 0;
    public string HomeSummary => health.IsPaused ? "今日已暫停" : $"已啟用 {EnabledCount} 項 · 下次提醒：{NextReminder}";
    public int PendingCount => health.Snapshot.Tools.Count(t => t.Pending is not null);
    public int TodayNotifications => health.Snapshot.Events.Where(e => e.TriggeredAt?.Date == health.Now.Date).Select(e => e.BatchId).Distinct().Count();
    public int TodayItems => health.Snapshot.Events.Count(e => e.TriggeredAt?.Date == health.Now.Date);
    public bool HasHistory => Timeline.Count > 0;
    public bool HasWeeklyHistory => Week.Any(d => d.Total > 0);
    public string PauseLabel => health.IsPaused ? "立即恢復" : "暫停今日提醒";
    public string BannerTitle => health.IsPaused ? "今日健康提醒已暫停" : pomodoro.State is { Status: "Running", Phase: "Focus" }
        ? $"專注中 · 預計 {health.Now + pomodoro.State.Remaining:HH:mm} 結束" : pomodoro.State.Status == "AwaitingConfirmation" ? "番茄鐘等待確認" : "照顧自己，從規律的小休息開始";
    public string BannerDetail => health.IsPaused ? "明天解除暫停，依各工具排程恢復；也可按「立即恢復」。" : health.Snapshot.EffectiveMode switch
    {
        "Override" => "覆蓋 · 即使番茄鐘專注中，仍依各工具排程提醒。",
        "AutoAlign" => health.Snapshot.AlignmentStage switch
        {
            "Focus" => "自動對齊 · 等待首次番茄鐘休息，一次提醒所有啟用中的工具。",
            "Break" => "自動對齊 · 首次休息結束後，重新起算各工具週期。",
            "Done" => "自動對齊 · 已完成一次對齊，後續依各工具的新週期提醒。",
            _ => "自動對齊 · 開始番茄鐘後，在首次休息時合併提醒並重設週期。"
        },
        _ => "延後 · 專注結束時合併到期項目，最多延後 30 分鐘，原始週期不變。"
    };
    public string NextReminder
    {
        get
        {
            if (health.IsPaused) return "已暫停";
            if (health.Snapshot.EffectiveMode == "AutoAlign" && health.Snapshot.AlignmentStage is "Focus" or "Break")
                return health.Snapshot.AlignmentStage == "Focus" ? "首次休息時" : "休息結束後起算";
            var times = health.Snapshot.Tools.Where(t => t.Options.Enabled).Select(t => t.Pending is { } p
                ? health.Snapshot.EffectiveMode == "Delay" && pomodoro.State is { Status: "Running", Phase: "Focus" }
                    ? new[] { p.ScheduledAt + HealthToolsService.MaximumDeferral, health.Now + pomodoro.State.Remaining }.Min()
                    : health.Now
                : t.NextAt).Where(t => t is not null).Select(t => t!.Value).ToArray();
            if (times.Length == 0) return "—";
            var next = times.Min();
            return next.Date == health.Now.Date ? next.ToString("HH:mm") : next.ToString("MM/dd HH:mm");
        }
    }
    public async Task LoadAsync()
    {
        try { await health.InitializeAsync(); foreach (var card in Cards) card.InitializeEditor(); Refresh(); }
        catch (Exception ex) { Message = "健康工具載入失敗：" + ex.Message; }
    }
    public void Refresh()
    {
        foreach (var card in Cards) card.Refresh(health.IsPaused);
        OnPropertyChanged(nameof(EnabledCount)); OnPropertyChanged(nameof(PendingCount)); OnPropertyChanged(nameof(TodayNotifications));
        OnPropertyChanged(nameof(TodayItems)); OnPropertyChanged(nameof(PauseLabel)); OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerDetail)); OnPropertyChanged(nameof(NextReminder));
        OnPropertyChanged(nameof(HasEnabledTools)); OnPropertyChanged(nameof(HomeSummary));
        if (ReferenceEquals(rendered, health.Snapshot) && renderedDate == health.Now.Date) return;
        rendered = health.Snapshot; renderedDate = health.Now.Date;
        RebuildHistory();
    }
    private void RebuildHistory()
    {
        var data = health.Snapshot;
        Timeline.Clear();
        var history = data.Events.Where(e => e.RecordedAt.Date == health.Now.Date).Select(e => new HealthTimelineRow(
            HealthTools.Name(e.Kind), $"原訂 {e.ScheduledAt:HH:mm}" + (e.TriggeredAt is { } at ? $" · 顯示 {at:HH:mm}" : ""), e.Result, e.RecordedAt));
        var pending = data.Tools.Where(t => t.Pending is not null).Select(t => new HealthTimelineRow(HealthTools.Name(t.Options.Kind),
            $"原訂 {t.Pending!.ScheduledAt:HH:mm}" + (data.EffectiveMode == "Delay" ? $" · 最晚 {t.Pending.ScheduledAt + HealthToolsService.MaximumDeferral:HH:mm}（專注延後）" : ""), "等待中", health.Now));
        foreach (var row in pending.Concat(history.OrderByDescending(e => e.At)).Take(ShowAllHistory ? int.MaxValue : 8)) Timeline.Add(row);
        Week.Clear();
        var days = Enumerable.Range(0, 7).Select(i => health.Now.Date.AddDays(i - 6)).ToArray();
        var counts = days.Select(d => HealthTools.Kinds.Select(k => data.Events.Count(e => e.Kind == k && e.TriggeredAt?.Date == d)).ToArray()).ToArray();
        var max = Math.Max(1, counts.Max(c => c.Sum()));
        for (var i = 0; i < 7; i++) Week.Add(new(days[i], counts[i], max));
        OnPropertyChanged(nameof(HasHistory)); OnPropertyChanged(nameof(HasWeeklyHistory));
    }
    private void EditTool(HealthToolOptions tool)
    {
        SelectedTabIndex = Array.IndexOf(HealthTools.Kinds, tool.Kind) + 1;
    }
    [RelayCommand] private void OpenSettings()
    {
        CoordinationMode = health.Snapshot.EffectiveMode switch { "AutoAlign" => "自動對齊", "Override" => "覆蓋", _ => "延後" }; SettingsOpen = true; Message = "";
        NotificationPosition = health.Snapshot.NotificationSide switch { "Left" => "左邊", "Center" => "置中", _ => "右邊" };
    }
    [RelayCommand] private void CloseSettings() => SettingsOpen = false;
    [RelayCommand] private async Task SaveSettingsAsync()
    {
        try
        {
            var side = NotificationPosition switch { "左邊" => "Left", "置中" => "Center", "右邊" => "Right", _ => throw new ArgumentException("請選擇左邊、置中或右邊。") };
            var mode = CoordinationMode switch { "自動對齊" => "AutoAlign", "覆蓋" => "Override", "延後" => "Delay", _ => throw new ArgumentException("請選擇番茄鐘協調模式。") };
            await health.SavePreferencesAsync(mode, side);
            SettingsOpen = false; Message = "已儲存健康提醒設定。"; Refresh();
        }
        catch (Exception ex) { Message = ex.Message; }
    }
    [RelayCommand] private async Task TogglePauseAsync()
    {
        try { await health.SetPausedAsync(!health.IsPaused); Refresh(); Message = health.IsPaused ? "今日已暫停，明天自動恢復。" : "已恢復，從下一個排程點提醒。"; }
        catch (Exception ex) { Message = ex.Message; }
    }
    [RelayCommand] private void ToggleHistory() { ShowAllHistory = !ShowAllHistory; RebuildHistory(); }
}

public partial class HealthToolCard(HealthToolsService health, string kind, Action<HealthToolOptions> edit) : ObservableObject
{
    private HealthToolState State => health.Snapshot.Tools.Single(t => t.Options.Kind == kind);
    private bool paused;
    private bool editorInitialized;
    private bool AwaitingAlignment => health.Snapshot.EffectiveMode == "AutoAlign" && health.Snapshot.AlignmentStage is "Focus" or "Break";
    private HealthToolsState? rendered;
    private DateTime renderedDate;
    [ObservableProperty] private HealthToolEditor? editor;
    [ObservableProperty] private string saveMessage = "";
    public ObservableCollection<HealthTimelineRow> History { get; } = [];
    public ObservableCollection<HealthWeekDay> Week { get; } = [];
    public bool HasHistory => History.Count > 0;
    public string Advice => HealthTools.Advice(kind);
    public int WeekCount => health.Snapshot.Events.Count(e => e.Kind == kind && e.TriggeredAt is not null);
    public string Name => HealthTools.Name(kind);
    public string Icon => kind switch { "Water" => "💧", "Sitting" => "🪑", "Screen" => "👁", _ => "🙆" };
    public string HomeIcon => kind switch
    {
        "Water" => "M12,2 C10,6 5,10 5,15 A7,7 0 0 0 19,15 C19,10 14,6 12,2 Z M8,15 C8,18 10,19 12,19",
        "Sitting" => "M6,3 L6,13 18,13 18,18 5,18 M7,18 L7,22 M17,18 L17,22 M6,7 L17,7 17,13",
        "Screen" => "M2,12 C7,3 17,3 22,12 C17,21 7,21 2,12 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12",
        _ => "M14,5 A2,2 0 1 1 10,5 A2,2 0 1 1 14,5 M12,9 L12,16 M4,8 L12,11 20,8 M12,16 L7,22 M12,16 L17,22"
    };
    public string Accent => kind switch { "Water" => "#309EDE", "Sitting" => "#39A67F", "Screen" => "#8C70D4", _ => "#D89231" };
    public bool Enabled => State.Options.Enabled;
    public double CardOpacity => Enabled ? 1 : .65;
    public string ToggleLabel => Enabled ? "停用" : "啟用";
    public string Status => !Enabled ? "尚未啟用" : paused ? "今日暫停" : State.Pending is not null ? "等待提醒" : AwaitingAlignment ? "等待對齊" : health.IsActive(State.Options) ? "排程中" : "時段外";
    public string Countdown => !Enabled ? "—" : paused ? "已暫停" : State.Pending is not null ? "待提醒" : AwaitingAlignment ? "待對齊" : State.NextAt is { } at ? $"{Math.Max(0, Math.Ceiling((at - health.Now).TotalMinutes)):0}" : "—";
    public string CountdownUnit => Enabled && !paused && !AwaitingAlignment && State.Pending is null ? "分鐘後提醒" : "";
    public string Schedule => AwaitingAlignment ? "首次休息結束後重新起算週期" : State.Pending is { } pending ? $"原訂 {pending.ScheduledAt:HH:mm}" + (health.Snapshot.EffectiveMode == "Delay" ? $" · 專注最多延後至 {pending.ScheduledAt.AddMinutes(30):HH:mm}" : "")
        : State.NextAt is { } at ? $"每 {State.Options.IntervalMinutes} 分鐘 · 下次 {at:MM/dd HH:mm}" : $"每 {State.Options.IntervalMinutes} 分鐘";
    public string Window => $"{State.Options.Start:HH:mm}–{State.Options.End:HH:mm} · " + (State.Options.DayMode == "Workdays" ? "工作日（依假日與補班）" : State.Options.DayMode == "Everyday" || State.Options.Days == 127 ? "每天" : State.Options.Days == 62 ? "週一至週五" : "自訂星期");
    public string Today => $"今日已提醒 {health.Snapshot.Events.Count(e => e.Kind == kind && e.TriggeredAt?.Date == health.Now.Date)} 個項目";
    public double Progress => !Enabled || paused ? 0 : State.Pending is not null ? 100 : State.NextAt is { } at && health.IsActive(State.Options)
        ? Math.Clamp(100 * (1 - (at - health.Now).TotalMinutes / State.Options.IntervalMinutes), 0, 100) : 0;
    [ObservableProperty] private string error = "";
    public void InitializeEditor()
    {
        if (editorInitialized) return;
        Editor = new(State.Options); editorInitialized = true;
    }
    public void Refresh(bool isPaused)
    {
        paused = isPaused;
        if (!ReferenceEquals(rendered, health.Snapshot) || renderedDate != health.Now.Date)
        {
            rendered = health.Snapshot; renderedDate = health.Now.Date;
            History.Clear();
            if (State.Pending is { } pending) History.Add(new(Name, $"原訂 {pending.ScheduledAt:MM/dd HH:mm}", "等待中", health.Now));
            foreach (var e in health.Snapshot.Events.Where(e => e.Kind == kind).OrderByDescending(e => e.RecordedAt))
                History.Add(new(Name, $"原訂 {e.ScheduledAt:MM/dd HH:mm}" + (e.TriggeredAt is { } at ? $" · 顯示 {at:HH:mm}" : ""), e.Result, e.RecordedAt));
            var dates = Enumerable.Range(0, 7).Select(i => health.Now.Date.AddDays(i - 6)).ToArray();
            var counts = dates.Select(d => health.Snapshot.Events.Count(e => e.Kind == kind && e.TriggeredAt?.Date == d)).ToArray();
            Week.Clear();
            for (var i = 0; i < 7; i++) Week.Add(new(dates[i], [counts[i], 0, 0, 0], Math.Max(1, counts.Max())));
        }
        OnPropertyChanged(string.Empty);
    }
    [RelayCommand] private async Task SaveAsync()
    {
        try
        {
            InitializeEditor(); await health.SaveToolAsync(Editor!.Build());
            Error = ""; SaveMessage = "已儲存，從下一個排程點開始提醒。"; Refresh(health.IsPaused);
        }
        catch (Exception ex) { Error = ex.Message; SaveMessage = ""; }
    }
    [RelayCommand] private void ResetDraft()
    {
        Editor = new(State.Options); editorInitialized = true; Error = ""; SaveMessage = "已還原為儲存的設定。";
    }
    [RelayCommand] private void Edit() => edit(State.Options);
    [RelayCommand] private async Task ToggleAsync()
    {
        try { await health.SaveToolAsync(State.Options with { Enabled = !Enabled }); if (Editor is not null) Editor.Enabled = Enabled; Error = ""; Refresh(health.IsPaused); }
        catch (Exception ex) { Error = ex.Message; }
    }
}

public partial class HealthToolEditor : ObservableObject
{
    private readonly string kind;
    public string Name => HealthTools.Name(kind);
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string interval = "60";
    [ObservableProperty] private string start = "08:30";
    [ObservableProperty] private string end = "18:00";
    public string[] DayModes { get; } = ["每天", "工作日", "自訂星期"];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsCustomDays)), NotifyPropertyChangedFor(nameof(DayModeHint))] private string dayMode = "自訂星期";
    public bool IsCustomDays => DayMode == "自訂星期";
    public string DayModeHint => DayMode == "工作日" ? "週一至週五，略過已載入的假日，補班日照常提醒。" : DayMode == "每天" ? "每天提醒，包含週末與假日。" : "依勾選的星期提醒，不隨假日或補班日調整。";
    public HealthDayChoice[] Days { get; }
    public HealthToolEditor(HealthToolOptions options)
    {
        kind = options.Kind; Enabled = options.Enabled; Interval = options.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
        Start = options.Start.ToString("HH:mm"); End = options.End.ToString("HH:mm");
        DayMode = options.DayMode switch { "Workdays" => "工作日", "Everyday" => "每天", _ => "自訂星期" };
        Days = Enumerable.Range(0, 7).Select(i => (i + 1) % 7).Select(i => new HealthDayChoice(i, (options.Days & (1 << i)) != 0)).ToArray();
    }
    public HealthToolOptions Build()
    {
        if (!int.TryParse(Interval, out var minutes) || !TimeOnly.TryParseExact(Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startAt)
            || !TimeOnly.TryParseExact(End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endAt))
            throw new ArgumentException("請輸入整數分鐘與 HH:mm 格式時間，例如 08:30。");
        var mode = DayMode switch { "每天" => "Everyday", "工作日" => "Workdays", "自訂星期" => "Custom", _ => throw new ArgumentException("請選擇提醒日期模式。") };
        var days = Days.Where(d => d.Selected).Sum(d => 1 << d.Day);
        var result = new HealthToolOptions { Kind = kind, Enabled = Enabled, IntervalMinutes = minutes, Start = startAt, End = endAt, DayMode = mode, Days = mode != "Custom" && days == 0 ? 62 : days };
        result.Validate(); return result;
    }
}
public partial class HealthDayChoice(int day, bool selected) : ObservableObject
{
    public int Day => day;
    public string Label => "日一二三四五六"[day].ToString();
    [ObservableProperty] private bool selected = selected;
}
public sealed record HealthTimelineRow(string Name, string Time, string Status, DateTime At);
public sealed record HealthWeekDay(DateTime Date, int[] Counts, int Maximum)
{
    public string Label => Date.ToString("MM/dd");
    public int Total => Counts.Sum();
    public string Detail => $"{Date:MM/dd} · 喝水 {Counts[0]}／久坐 {Counts[1]}／螢幕休息 {Counts[2]}／伸展 {Counts[3]} 個項目";
    public double WaterHeight => 110d * Counts[0] / Maximum;
    public double SittingHeight => 110d * Counts[1] / Maximum;
    public double ScreenHeight => 110d * Counts[2] / Maximum;
    public double StretchHeight => 110d * Counts[3] / Maximum;
}
