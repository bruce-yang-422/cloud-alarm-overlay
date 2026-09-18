using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Recurrence;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.ViewModels;

public partial class CountdownsViewModel(ICountdownRepository repository, TimeProvider clock, IUserDialogs dialogs, ChangeSignal changes, ILunarCalendarRepository? lunar = null, IHolidayRepository? holidays = null) : ObservableObject
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CountdownItem? editing;
    private IReadOnlyDictionary<DateOnly,int> lunarCache = new Dictionary<DateOnly,int>();
    private IReadOnlyList<Holiday> holidayCache = [];
    public ObservableCollection<CountdownRow> Items { get; } = [];
    public ObservableCollection<CountdownRow> Pinned { get; } = [];
    public ObservableCollection<CountdownRow> VisibleItems { get; } = [];
    public ObservableCollection<CountdownRow> Timeline { get; } = [];
    public string[] ViewModes { get; } = ["卡片", "資料表"];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCardView), nameof(IsTableView))]
    private string viewMode = "卡片";
    public bool IsCardView => ViewMode != "資料表";
    public bool IsTableView => ViewMode == "資料表";
    public string[] Filters { get; } = ["全部", "進行中", "7 天內", "已到期", "已完成", "已釘選"];
    [ObservableProperty] private string filter = "全部";
    public string[] CategoryFilters { get; } = ["全部分類", "工作", "生活", "節日", "旅行", "其他"];
    [ObservableProperty] private string categoryFilter = "全部分類";
    partial void OnCategoryFilterChanged(string value) => UpdateDashboard();
    public string[] Categories { get; } = ["工作", "生活", "節日", "旅行", "其他"];
    public string[] Repeats { get; } = ["不重複", "每天", "每個工作日", "每週", "每月", "每年（國曆）", "每月農曆", "每年（農曆）"];
    public DayChoice[] Weekdays { get; } = Enumerable.Range(1,7).Select(i=>new DayChoice(i,new[]{"一","二","三","四","五","六","日"}[i-1]) { IsSelected=i<=5 }).ToArray();
    public DayChoice[] Months { get; } = Enumerable.Range(1,12).Select(i=>new DayChoice(i,$"{i} 月") { IsSelected=true }).ToArray();
    public DayChoice[] LunarDays { get; } = Enumerable.Range(1,30).Select(i=>new DayChoice(i,$"{i} 日") { IsSelected=i is 1 or 15 }).ToArray();
    public int[] AnnualMonths { get; } = Enumerable.Range(1,12).ToArray();
    [ObservableProperty] private int annualMonth = 1;
    [ObservableProperty] private int annualDay = 1;
    public bool IsAnnual => Repeat is "每年（國曆）" or "每年";
    public int[] MonthDays { get; } = Enumerable.Range(1,31).ToArray();
    public int[] LunarMonths { get; } = Enumerable.Range(1,12).ToArray();
    public int[] LunarDates { get; } = Enumerable.Range(1,30).ToArray();
    [ObservableProperty] private int monthDay = 1;
    [ObservableProperty] private int lunarMonth = 1;
    [ObservableProperty] private int lunarDate = 1;
    [ObservableProperty] private bool includeLeapMonth;
    [ObservableProperty] private bool skipOnHoliday;
    public bool IsWeekly => Repeat=="每週";
    public bool IsMonthly => Repeat=="每月";
    public bool IsLunar => Repeat=="每月農曆";
    public bool IsLunarDate => Repeat is "每年（農曆）" or "指定農曆月日";
    partial void OnRepeatChanged(string value)
    {
        OnPropertyChanged(nameof(DateLabel));
        if(IsAnnual){AnnualMonth=(TargetDate??DateTime.Today).Month;AnnualDay=(TargetDate??DateTime.Today).Day;}
        if(value=="每月") MonthDay=(TargetDate??DateTime.Today).Day;
        foreach(var name in new[]{nameof(IsAnnual),nameof(IsWeekly),nameof(IsMonthly),nameof(IsLunar),nameof(IsLunarDate)}) OnPropertyChanged(name);
    }
    private string BuildRecurrence(DateTime target)
    {
        var months=Months.Where(m=>m.IsSelected).Select(m=>m.Value).ToArray();
        if(IsMonthly && months.Length==0)throw new ArgumentException("請至少選擇一個月份。");
        if(IsAnnual && (AnnualMonth is <1 or >12 || AnnualDay<1 || AnnualDay>DateTime.DaysInMonth(2000,AnnualMonth)))
            throw new ArgumentException("請選擇有效的國曆月日；2 月 29 日只在閏年提醒。");
        var rule=Repeat switch
        {
            "每天"=>"Daily", "每個工作日"=>"Weekly:1,2,3,4,5",
            "每週"=>"Weekly:"+string.Join(",",Weekdays.Where(d=>d.IsSelected).Select(d=>d.Value)),
            "每月"=>$"Monthly:{MonthDay}"+(months.Length==12?"":":"+string.Join(",",months)),
            "每年（國曆）"=>$"Monthly:{AnnualDay}:{AnnualMonth}",
            "每年"=>$"Monthly:{target.Day}:{target.Month}",
            "每月農曆"=>"LunarDay:"+string.Join(",",LunarDays.Where(d=>d.IsSelected).Select(d=>d.Value)),
            "每年（農曆）" or "指定農曆月日"=>$"LunarDate:{LunarMonth}:{LunarDate}:"+(IncludeLeapMonth?"Both":"Regular"), _=>"None"
        };
        RecurrenceRule.Validate(rule); return rule;
    }
    public string[] Reminders { get; } = ["不提醒", "當天", "提前 1 天", "提前 3 天", "提前 7 天"];
    private static readonly int[] ReminderValues = [-1, 0, 1, 3, 7];
    [ObservableProperty] private string category = "工作";
    [ObservableProperty] private string repeat = "不重複";
    [ObservableProperty] private string reminder = "不提醒";
    [ObservableProperty] private string reminderTime = "09:00";
    [ObservableProperty] private string notes = "";
    public bool HasReminder => Reminder != "不提醒";
    partial void OnReminderChanged(string value) => OnPropertyChanged(nameof(HasReminder));
    public ObservableCollection<int> Years { get; } = [];
    [ObservableProperty] private int selectedYear = clock.GetLocalNow().Year;
    private DateTime dashboardNow = clock.GetLocalNow().DateTime;
    partial void OnSelectedYearChanged(int value) => UpdateDashboard();
    [ObservableProperty] private int activeCount;
    [ObservableProperty] private int completedCount;
    [ObservableProperty] private int workCount;
    [ObservableProperty] private int lifeCount;
    [ObservableProperty] private int holidayCount;
    [ObservableProperty] private int travelCount;
    [ObservableProperty] private int otherCount;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(YearRemainingPercent))] private double yearPercent;
    public double YearRemainingPercent => 100 - YearPercent;
    [ObservableProperty] private string yearProgressLabel = "";
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int dueSoonCount;
    [ObservableProperty] private int expiredCount;
    [ObservableProperty] private int pinnedCount;
    [ObservableProperty] private double chartScale = 1;
    public bool IsTimelineEmpty => Timeline.Count == 0;
    public bool IsFilteredEmpty => VisibleItems.Count == 0;
    partial void OnFilterChanged(string value) => UpdateDashboard();

    private void UpdateDashboard()
    {
        if (SelectedYear is < 1 or > 9999) return;
        var scoped = Items.Where(i => i.DisplayDate.Year == SelectedYear).ToArray();
        TotalCount = scoped.Length;
        ActiveCount = scoped.Count(i => !i.IsExpired && !i.Item.IsCompleted);
        CompletedCount = scoped.Count(i => i.Item.IsCompleted);
        DueSoonCount = Items.Count(i => !i.IsScheduleUnavailable && !i.Item.IsCountUp && !i.IsExpired && !i.Item.IsCompleted && i.RemainingDays <= 7);
        ExpiredCount = scoped.Count(i => i.IsExpired);
        WorkCount = scoped.Count(i => i.Item.Category == "工作");
        LifeCount = scoped.Count(i => i.Item.Category == "生活");
        HolidayCount = scoped.Count(i => i.Item.Category == "節日");
        TravelCount = scoped.Count(i => i.Item.Category == "旅行");
        OtherCount = scoped.Count(i => i.Item.Category == "其他");
        var days = DateTime.IsLeapYear(SelectedYear) ? 366 : 365;
        var elapsed = dashboardNow.Year < SelectedYear ? 0 : dashboardNow.Year > SelectedYear ? days : dashboardNow.DayOfYear;
        YearPercent = 100d * elapsed / days;
        YearProgressLabel = $"{SelectedYear} 年 · 第 {elapsed} / {days} 天";
        ReplaceWhenChanged(Pinned, Items.Where(i => i.Item.IsPinned).OrderBy(i => i.DisplayDate).ThenBy(i => i.Title).ThenBy(i => i.Item.Id).ToArray());
        PinnedCount = Pinned.Count;
        foreach (var year in Items.Select(i => i.DisplayDate.Year).Append(dashboardNow.Year).Append(Math.Min(9999, dashboardNow.Year + 1)).Distinct().Order())
            if (!Years.Contains(year)) Years.Add(year);
        var visible = Items.Where(i => CategoryFilter == "全部分類" || i.Item.Category == CategoryFilter).Where(i => Filter switch
        {
            "進行中" => !i.Item.IsCompleted && !i.IsExpired,
            "7 天內" => !i.IsScheduleUnavailable && !i.Item.IsCountUp && !i.IsExpired && !i.Item.IsCompleted && i.RemainingDays <= 7,
            "已完成" => i.Item.IsCompleted,
            "已到期" => i.IsExpired,
            "已釘選" => i.Item.IsPinned,
            _ => true
        }).OrderByDescending(i => i.Item.IsTop).ThenBy(i => i.DisplayDate).ThenBy(i => i.Title).ThenBy(i => i.Item.Id).ToArray();
        ReplaceWhenChanged(VisibleItems, visible);
        var timeline = Items.Where(i => !i.IsScheduleUnavailable && !i.Item.IsCountUp && !i.IsExpired && !i.Item.IsCompleted).OrderBy(i => i.DisplayDate).Take(5).ToArray();
        ReplaceWhenChanged(Timeline, timeline);
        ChartScale = Math.Max(1, Math.Ceiling(timeline.Select(i => i.RemainingDays).DefaultIfEmpty().Max()));
        OnPropertyChanged(nameof(IsFilteredEmpty)); OnPropertyChanged(nameof(IsTimelineEmpty));
    }

    private static void ReplaceWhenChanged(ObservableCollection<CountdownRow> collection, CountdownRow[] rows)
    {
        if (collection.SequenceEqual(rows)) return;
        collection.Clear(); foreach (var row in rows) collection.Add(row);
    }
    public string[] Directions { get; } = ["倒數", "正數"];
    public string[] DisplayFormats { get; } = ["天數", "週＋天", "月＋天", "年＋月＋天"];
    private static readonly string[] FormatValues = ["Days", "WeeksDays", "MonthsDays", "YearsMonthsDays"];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DateLabel), nameof(IsCountUp))] private string direction = "倒數";
    [ObservableProperty] private string displayFormat = "天數";
    public bool IsCountUp => Direction == "正數";
    public string DateLabel => IsCountUp ? "起始日期" : Repeat=="不重複"?"目標日期":"開始日期";
    public string[] Modes { get; } = ["倒數日", "倒數時間"];
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string mode = "倒數日";
    [ObservableProperty] private DateTime? targetDate = clock.GetLocalNow().Date.AddDays(1);
    [ObservableProperty] private string targetTime = "09:00";
    [ObservableProperty] private bool pinOnHome;
    [ObservableProperty] private string message = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewCommand), nameof(EditCommand), nameof(SaveCommand), nameof(TogglePinCommand), nameof(ToggleTopCommand), nameof(ToggleCompleteCommand), nameof(DeleteCommand))]
    private bool isBusy;
    private bool CanEdit => !IsBusy;
    public bool IsTimeMode => Mode == "倒數時間";
    public bool IsEmpty => Items.Count == 0;
    public bool IsPinnedEmpty => Pinned.Count == 0;
    public string SaveLabel => editing is null ? "建立事件" : "儲存變更";
    public string EditorHeading => editing is null ? "新增事件" : "編輯事件";
    partial void OnModeChanged(string value) => OnPropertyChanged(nameof(IsTimeMode));

    public async Task LoadAsync()
    {
        await gate.WaitAsync();
        try { await ReloadAsync(); }
        catch (Exception ex) { Message = "讀取倒數失敗：" + ex.Message; }
        finally { gate.Release(); }
    }

    private async Task ReloadAsync()
    {
        lunarCache=lunar is null?new Dictionary<DateOnly,int>():(await lunar.GetAllAsync()).ToDictionary(e=>e.Date,e=>e.LunarDay);
        holidayCache=holidays is null?[]:await holidays.GetAllAsync();
        var saved = await repository.GetAllAsync();
        Items.Clear(); Pinned.Clear();
        var now = clock.GetLocalNow().DateTime;
        foreach (var item in saved.OrderBy(i => i.TargetAt).ThenBy(i => i.Title).ThenBy(i => i.Id))
        {
            var row = new CountdownRow(item); row.Update(now,lunarCache,holidayCache);
            Items.Add(row);
            if (item.IsPinned) Pinned.Add(row);
        }
        dashboardNow = now;
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(IsPinnedEmpty));
        UpdateDashboard();
    }

    public void Update(DateTime now)
    {
        dashboardNow = now;
        foreach (var row in Items) row.Update(now,lunarCache,holidayCache);
        UpdateDashboard();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void New()
    {
        editing = null; Title = ""; Mode = "倒數日"; Direction = "倒數"; DisplayFormat = "天數";
        TargetDate = clock.GetLocalNow().Date.AddDays(1); TargetTime = "09:00";
        Category = "工作"; Repeat = "不重複"; Reminder = "不提醒"; ReminderTime = "09:00"; Notes = "";
        MonthDay=(TargetDate??DateTime.Today).Day; LunarMonth=1; LunarDate=1; IncludeLeapMonth=false; SkipOnHoliday=false;
        foreach(var m in Months)m.IsSelected=true;
        foreach(var d in Weekdays)d.IsSelected=d.Value<=5;
        foreach(var d in LunarDays)d.IsSelected=d.Value is 1 or 15;
        PinOnHome = false; Message = ""; OnPropertyChanged(nameof(EditorHeading)); OnPropertyChanged(nameof(SaveLabel));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit(CountdownRow row)
    {
        editing = row.Item; Title = editing.Title; Mode = editing.Mode == "Days" ? "倒數日" : "倒數時間";
        Direction = editing.IsCountUp ? "正數" : "倒數"; DisplayFormat = DisplayFormats[Array.IndexOf(FormatValues, editing.DisplayFormat)];
        TargetDate = editing.TargetAt.Date; TargetTime = editing.TargetAt.ToString("HH:mm");
        Category = editing.Category;
        var parts=editing.EffectiveRecurrence.Split(':');
        Repeat=parts[0] switch { "Daily"=>"每天", "Weekly"=>parts[1]=="1,2,3,4,5"?"每個工作日":"每週", "Monthly"=>parts.Length==3 && !parts[2].Contains(',')?"每年（國曆）":"每月", "LunarDay"=>"每月農曆", "LunarDate"=>"每年（農曆）", _=>"不重複" };
        foreach(var m in Months)m.IsSelected=parts[0]!="Monthly"||parts.Length<3||parts[2].Split(',').Contains(m.Value.ToString());
        foreach(var d in Weekdays)d.IsSelected=parts[0]=="Weekly"&&parts[1].Split(',').Contains(d.Value.ToString());
        foreach(var d in LunarDays)d.IsSelected=parts[0]=="LunarDay"&&parts[1].Split(',').Contains(d.Value.ToString());
        if(parts[0]=="Monthly"){MonthDay=int.Parse(parts[1]);if(IsAnnual){AnnualDay=MonthDay;AnnualMonth=int.Parse(parts[2]);}}
        if(parts[0]=="LunarDate"){LunarMonth=int.Parse(parts[1]);LunarDate=int.Parse(parts[2]);IncludeLeapMonth=parts.Length==4&&parts[3]=="Both";}
        SkipOnHoliday=editing.SkipOnHoliday;
        Reminder = Reminders[Array.IndexOf(ReminderValues, editing.ReminderDays)];
        ReminderTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(editing.ReminderMinutes)).ToString("HH:mm");
        Notes = editing.Notes;
        PinOnHome = editing.IsPinned; Message = ""; OnPropertyChanged(nameof(EditorHeading)); OnPropertyChanged(nameof(SaveLabel));
    }

    public event EventHandler? Saved;

    public CountdownRow CreateEditorPreview()
    {
        if (TargetDate is not {} date) throw new ArgumentException("請選擇日期");
        var time = TimeSpan.Zero;
        if (IsTimeMode)
        {
            if (!TimeOnly.TryParseExact(TargetTime.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new ArgumentException("請輸入有效時間，例如 09:30");
            time = parsed.ToTimeSpan();
        }
        var minutes = 540;
        if (HasReminder)
        {
            if (!TimeOnly.TryParseExact(ReminderTime.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new ArgumentException("請輸入有效提醒時間");
            minutes = parsed.Hour * 60 + parsed.Minute;
        }
        var now = clock.GetLocalNow().DateTime;
        var item = new CountdownItem
        {
            Title = string.IsNullOrWhiteSpace(Title) ? "我的紀念事件" : Title.Trim(),
            TargetAt = date.Date + time, CreatedAt = editing?.CreatedAt ?? now,
            Mode = IsTimeMode ? "Time" : "Days", Direction = IsCountUp ? "Up" : "Down",
            DisplayFormat = FormatValues[Array.IndexOf(DisplayFormats, DisplayFormat)],
            Category = Category, Notes = Notes, IsPinned = PinOnHome, Recurrence = BuildRecurrence(date), SkipOnHoliday = SkipOnHoliday,
            ReminderDays = ReminderValues[Array.IndexOf(Reminders, Reminder)], ReminderMinutes = minutes
        };
        item.Validate();
        var row = new CountdownRow(item);
        row.Update(now, lunarCache, holidayCache);
        return row;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        var saved = await MutateAsync(async () =>
        {
            if (TargetDate is not {} date) throw new ArgumentException("請選擇目標日期。");
            var time = TimeSpan.Zero;
            if (IsTimeMode)
            {
                if (!TimeOnly.TryParseExact(TargetTime.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    throw new ArgumentException("時間請使用 HH:mm，例如 09:30。");
                time = parsed.ToTimeSpan();
            }
            var basis = editing is null ? new CountdownItem { CreatedAt = clock.GetLocalNow().DateTime }
                : (await repository.GetAllAsync()).SingleOrDefault(i => i.Id == editing.Id)
                    ?? throw new InvalidOperationException("項目已移除，請重新新增。");
            var reminderMinutes = basis.ReminderMinutes;
            if (HasReminder)
            {
                if (!TimeOnly.TryParseExact(ReminderTime.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    throw new ArgumentException("提醒時間請使用 HH:mm，例如 14:30。");
                reminderMinutes = parsed.Hour * 60 + parsed.Minute;
            }
            var item = basis with
            {
                Direction = IsCountUp ? "Up" : "Down", DisplayFormat = FormatValues[Array.IndexOf(DisplayFormats, DisplayFormat)],
                Title = Title.Trim(), TargetAt = date.Date + time, Mode = IsTimeMode ? "Time" : "Days", IsPinned = PinOnHome,
                Category = Category, Repeat = Repeat switch { "每週"=>"Weekly", "每月"=>"Monthly", "每年" or "每年（國曆）"=>"Yearly", _=>"None" },
                Recurrence=BuildRecurrence(date), SkipOnHoliday=SkipOnHoliday,
                ReminderDays = ReminderValues[Array.IndexOf(Reminders, Reminder)], ReminderMinutes = reminderMinutes, Notes = Notes.Trim()
            };
            if (item.ReminderDays == 0 && item.Mode == "Time" && item.ReminderMinutes > item.TargetAt.TimeOfDay.TotalMinutes)
                throw new ArgumentException("當天提醒時間不可晚於目標時間。");
            if (editing is null || item.TargetAt != basis.TargetAt || item.Mode != basis.Mode || item.Repeat != basis.Repeat || item.Recurrence != basis.Recurrence || item.SkipOnHoliday != basis.SkipOnHoliday ||
                item.ReminderDays != basis.ReminderDays || item.ReminderMinutes != basis.ReminderMinutes)
                item = item with { ReminderChangedAt = clock.GetLocalNow().DateTime };
            await repository.SaveAsync(item);
            New();
        }, "倒數已儲存。");
        if (saved) Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task TogglePinAsync(CountdownRow row)
        => await MutateAsync(async () =>
        {
            var current = (await repository.GetAllAsync()).SingleOrDefault(i => i.Id == row.Item.Id)
                ?? throw new InvalidOperationException("項目已移除，請重新選擇。");
            await repository.SaveAsync(current with { IsPinned = !current.IsPinned });
            if (editing?.Id == current.Id) PinOnHome = !current.IsPinned;
        }, "首頁釘選已更新。");

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ToggleTopAsync(CountdownRow row)
        => await MutateAsync(async () =>
        {
            var current = (await repository.GetAllAsync()).SingleOrDefault(i => i.Id == row.Item.Id)
                ?? throw new InvalidOperationException("項目已移除，請重新選擇。");
            await repository.SaveAsync(current with { IsTop = !current.IsTop });
        }, "清單置頂已更新。");

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ToggleCompleteAsync(CountdownRow row)
        => await MutateAsync(async () =>
        {
            var current = (await repository.GetAllAsync()).SingleOrDefault(i => i.Id == row.Item.Id)
                ?? throw new InvalidOperationException("項目已移除，請重新選擇。");
            await repository.SaveAsync(current with { CompletedAt = current.IsCompleted ? null : clock.GetLocalNow().DateTime,
                ReminderChangedAt = clock.GetLocalNow().DateTime });
        }, "完成狀態已更新；已完成事件不再提醒。");

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task DeleteAsync(CountdownRow row)
    {
        if (!dialogs.Confirm($"確定刪除倒數「{row.Item.Title}」？首頁釘選也會移除。")) return;
        await MutateAsync(async () =>
        {
            await repository.DeleteAsync(row.Item.Id);
            if (editing?.Id == row.Item.Id) New();
        }, "倒數已刪除。");
    }

    private async Task<bool> MutateAsync(Func<Task> action, string success)
    {
        await gate.WaitAsync(); IsBusy = true;
        try { await action(); await ReloadAsync(); Message = success; changes.Notify(); return true; }
        catch (Exception ex) { Message = ex.Message; return false; }
        finally { IsBusy = false; gate.Release(); }
    }
}

public partial class CountdownRow(CountdownItem item) : ObservableObject
{
    public CountdownItem Item { get; } = item;
    public string Title => Item.Title;
    public string CompleteLabel => Item.IsCompleted ? "恢復進行" : "標記完成";
    public string DetailsLabel => Item.Category + " · " + RecurrenceRule.Describe(Item.EffectiveRecurrence) +
        (Item.ReminderDays < 0 ? " · 不提醒" : $" · {(Item.ReminderDays == 0 ? "當天" : $"提前 {Item.ReminderDays} 天")} {Item.ReminderMinutes / 60:00}:{Item.ReminderMinutes % 60:00}");
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TargetLabel), nameof(DateCaption))] private DateTime displayDate;
    public string TargetLabel => DisplayDate.ToString(Item.Mode == "Days" ? "yyyy/MM/dd" : "yyyy/MM/dd HH:mm");
    public string ModeIcon => Item.Mode == "Days" ? "\uE787" : "\uE823";
    public string ModeLabel => (Item.IsCountUp ? "正數" : "倒數") + (Item.Mode == "Days" ? "日" : "時間");
    public bool ShowProgress => !Item.IsCountUp && !IsScheduleUnavailable;
    public string DateCaption => (Item.IsCountUp ? "起始：" : "目標：") + TargetLabel;
    [ObservableProperty] private string homeSummary = "";
    public string PinLabel => Item.IsPinned ? "取消釘選" : "釘選首頁";
    public string TopLabel => Item.IsTop ? "取消置頂" : "置頂";
    public string PlacementLabel => (Item.IsTop ? "↑ 置頂" : "") + (Item.IsPinned ? "  ◆ 首頁" : "");
    public bool HasNotes => !string.IsNullOrWhiteSpace(Item.Notes);
    public bool HasHomeClock => Item.Mode == "Time";
    [ObservableProperty] private bool showHomeCountdown;
    [ObservableProperty] private string homeDays = "0";
    [ObservableProperty] private string homeHours = "00";
    [ObservableProperty] private string homeMinutes = "00";
    [ObservableProperty] private string homeSeconds = "00";
    [ObservableProperty] private string homeRemainingDetail = "";
    [ObservableProperty] private string homeElapsedLabel = "";
    [ObservableProperty] private string remaining = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowProgress))] private bool isScheduleUnavailable;
    [ObservableProperty] private bool isExpired;
    [ObservableProperty] private double remainingDays;
    [ObservableProperty] private double elapsedPercent;
    [ObservableProperty] private string statusLabel = "";
    [ObservableProperty] private string progressLabel = "";
    public bool HasNextReminderLabel => !string.IsNullOrEmpty(NextReminderLabel);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNextReminderLabel))] private string nextReminderLabel = "";
    public void Update(DateTime now, IReadOnlyDictionary<DateOnly,int>? lunarDays = null, IReadOnlyList<Holiday>? holidays = null)
    {
        DisplayDate = Item.DisplayTarget(now,lunarDays,holidays);
        Remaining = Item.Remaining(now,lunarDays,holidays);
        NextReminderLabel = Item.IsCompleted ? "已完成，提醒已停止" : Item.ReminderDays < 0 ? "" :
            Item.NextReminder(now,lunarDays,holidays) is {} next ? $"下次提醒：{next:yyyy/MM/dd HH:mm}" : "沒有待發提醒，請檢查開始日期、農曆資料與規則";
        var at = Item.Mode == "Days" ? now.Date : now;
        var start = Item.Mode == "Days" ? Item.CreatedAt.Date : Item.CreatedAt;
        IsScheduleUnavailable = !Item.IsCompleted && !Item.IsCountUp && Item.EffectiveRecurrence!="None" && Item.NextTarget(at,lunarDays,holidays) is null;
        IsExpired = !IsScheduleUnavailable && !Item.IsCountUp && !Item.IsCompleted && (Item.Mode == "Days" ? now.Date > DisplayDate.Date : now >= DisplayDate);
        RemainingDays = Math.Max(0, (DisplayDate - at).TotalDays);
        var duration = (DisplayDate - start).TotalSeconds;
        ElapsedPercent = duration > 0 ? Math.Clamp((at-start).TotalSeconds / duration * 100, 0, 100) : at >= DisplayDate ? 100 : 0;
        StatusLabel = Item.IsCompleted ? "已完成" : IsScheduleUnavailable ? "待確認日期" : Item.IsCountUp ? (at < Item.TargetAt ? "尚未開始" : "累計中") : IsExpired ? "已到期" : RemainingDays == 0 ? "就是今天" : RemainingDays <= 7 ? "即將到來" : "倒數中";
        ProgressLabel = $"時間已過 {ElapsedPercent:0}%";
        ShowHomeCountdown = !Item.IsCompleted && !IsExpired;
        var seconds = (long)Math.Ceiling(Math.Max(0, (DisplayDate - now).TotalSeconds));
        HomeDays = (Item.Mode == "Days" ? Math.Max(0, (DisplayDate.Date - now.Date).Days) : seconds / 86400).ToString(CultureInfo.InvariantCulture);
        HomeHours = (seconds % 86400 / 3600).ToString("00");
        HomeMinutes = (seconds % 3600 / 60).ToString("00");
        HomeSeconds = (seconds % 60).ToString("00");
        HomeRemainingDetail = Item.IsCompleted ? "事件已標記完成" : IsExpired ? "已到達目標時間" : Item.Mode == "Days"
            ? $"距離目標還有 {HomeDays} 天" : $"距離目標還有 {HomeDays} 天 {HomeHours} 小時 {HomeMinutes} 分 {HomeSeconds} 秒";
        HomeElapsedLabel = $"已經過 {ElapsedPercent:0}%";
        if (Item.IsCountUp)
        {
            var until = Item.CompletedAt ?? now;
            HomeSummary = (Item.Mode == "Days" ? until.Date : until) < Item.TargetAt ? "尚未開始" :
                "已過 " + CountdownItem.FormatInterval(Item.TargetAt, Item.Mode == "Days" ? until.Date : until, Item.DisplayFormat, Item.Mode == "Time", true);
            if (Item.IsCompleted) HomeSummary = "已完成 · " + HomeSummary;
            var elapsed = (Item.Mode == "Days" ? until.Date : until) - Item.TargetAt;
            var wholeSeconds = (long)Math.Floor(Math.Max(0, elapsed.TotalSeconds));
            HomeDays = (wholeSeconds / 86400).ToString(CultureInfo.InvariantCulture);
            HomeHours = (wholeSeconds % 86400 / 3600).ToString("00");
            HomeMinutes = (wholeSeconds % 3600 / 60).ToString("00");
            HomeSeconds = (wholeSeconds % 60).ToString("00");
            HomeRemainingDetail = HomeSummary; HomeElapsedLabel = ""; ElapsedPercent = 0;
            ShowHomeCountdown = !Item.IsCompleted && elapsed >= TimeSpan.Zero;
            ProgressLabel = "正數累計";
        }
        else HomeSummary = IsScheduleUnavailable || Item.IsCompleted || IsExpired || Item.Mode == "Days" || Item.DisplayFormat != "Days" ? Remaining
            : $"剩餘 {HomeDays} 天 {HomeHours}:{HomeMinutes}:{HomeSeconds}";
    }
}
