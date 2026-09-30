using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Core.Repositories;
using System.Globalization;

namespace CloudAlarmOverlay.App.ViewModels;

public sealed record CalendarEntry(AlarmTask Task,DateTime Start,DateTime End,bool AllDay)
{
    public DateTime LastDay=>End>Start?End.AddTicks(-1).Date:Start.Date;
    public string Title=>Task.Title;
    public string TimeLabel=>AllDay?"全天":Start.ToString("HH:mm");
    public string RoutineHint=>Task.Enabled?CloudAlarmOverlay.Core.Recurrence.RecurrenceRule.Describe(Task.Recurrence):"提醒已關閉";
    public string Period=>AllDay?$"{Start:yyyy/MM/dd} ～ {LastDay:yyyy/MM/dd}（全天，含結束日）":End>Start?$"{Start:yyyy/MM/dd HH:mm} ～ {End:yyyy/MM/dd HH:mm}":$"{Start:yyyy/MM/dd HH:mm}";
    public string Reminder=>!Task.Enabled?"提醒已關閉；活動仍保留在月曆":$"{(Task.Recurrence=="None"?Task.ScheduledAt:Start):yyyy/MM/dd HH:mm} · {Task.Level}";
    public string Source=>Task.Source.StartsWith("Google:")?"Google 日曆／試算表":Task.Source;
    public bool CanEdit=>Task.Source==TaskSources.Local||Task.IsGoogleCalendar;
    public string EditLabel=>Task.IsGoogleCalendar?"調整本機提醒":"編輯活動／提醒";
    public string PreviewHint=>Period+"\n雙擊查看詳細資料";
}
public sealed record CalendarDay(DateTime Date,bool InMonth,IReadOnlyList<CalendarEntry> Events)
{
    public Holiday? Holiday {get;init;}
    public bool IsMakeupDay=>Holiday?.Type=="補班日";
    public bool IsRedDay=>!IsMakeupDay&&(Holiday is not null||Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    public string DayNote=>IsMakeupDay?"補班":"";
    public string HolidayName=>Holiday is null?"":IsMakeupDay?"補班日":string.IsNullOrWhiteSpace(Holiday.Note)?Holiday.Type:Holiday.Note;
    public string SolarTerm {get;init;}="";
    public string LunarDate=>MainViewModel.LocalLunarDate(Date);
    public string LunarLabel=>LunarDate.Contains('月')?(LunarDate.EndsWith("初一")?LunarDate[..^2]:LunarDate[^2..]):"—";
    public string Almanac=>string.Join(" · ",new[]{"農曆 "+LunarDate,HolidayName,SolarTerm}.Where(s=>s.Length>0));
    public int Number=>Date.Day;
    public bool Today=>Date.Date==DateTime.Today;
    public string Label=>Date.ToString("yyyy/MM/dd ddd",CultureInfo.GetCultureInfo("zh-TW"))+" · "+Almanac;
    public int HiddenCount {get;init;}
    public string More=>HiddenCount>0?$"共 {Events.Count} 項 ›":"";
}
public sealed record CalendarSegment(CalendarEntry Entry,int Column,int Span,int Lane,bool ContinuesBefore,bool ContinuesAfter)
{
    public string Caption=>(ContinuesBefore?"← ":Entry.AllDay?"":$"{Entry.Start:HH:mm} ")+Entry.Title+(ContinuesAfter?" →":"");
}
public sealed record CalendarWeek(IReadOnlyList<CalendarDay> Days,IReadOnlyList<CalendarSegment> Segments);

public sealed record CalendarWeekday(int Value,string Label)
{
    public override string ToString()=>Label;
    public string ShortLabel=>Label[^1..];
    public bool IsWeekend=>Value is 0 or 6;
}
public partial class TaskCalendarViewModel(ITaskSchedulingService scheduling,ISettingsRepository? settings=null,IHolidayRepository? holidays=null,ILunarCalendarRepository? lunar=null):ObservableObject
{
    public const string WeekStartSettingKey="TaskCalendarWeekStart";
    public IReadOnlyList<CalendarWeekday> WeekStartOptions {get;}=Enumerable.Range(0,7).Select(i=>new CalendarWeekday(i,"星期"+"日一二三四五六"[i])).ToArray();
    public IReadOnlyList<CalendarWeekday> Weekdays=>Enumerable.Range(0,7).Select(i=>WeekStartOptions[(i+WeekStart)%7]).ToArray();
    [ObservableProperty,NotifyPropertyChangedFor(nameof(Weekdays))] private int weekStart;
    [ObservableProperty] private string preferenceMessage="";
    private bool loadingPreferences;
    private readonly SemaphoreSlim preferenceGate=new(1,1);
    public Task PendingSave {get;private set;}=Task.CompletedTask;
    partial void OnWeekStartChanged(int value)
    {
        PendingBuild=BuildAsync();
        if(!loadingPreferences)PendingSave=SaveWeekStartAsync();
    }
    public async Task LoadPreferencesAsync()
    {
        if(settings is null)return;
        loadingPreferences=true;
        try{var saved=await settings.GetAsync(WeekStartSettingKey);WeekStart=int.TryParse(saved?.Value,out var day)&&day is >=0 and <=6?day:0;}
        finally{loadingPreferences=false;}
    }
    private async Task SaveWeekStartAsync()
    {
        if(settings is null)return;
        await preferenceGate.WaitAsync();
        try{await settings.SaveAsync(new Setting{Key=WeekStartSettingKey,Value=WeekStart.ToString(CultureInfo.InvariantCulture)});PreferenceMessage="";}
        catch(Exception){PreferenceMessage="起始日未能儲存，請重新選擇。";}
        finally{preferenceGate.Release();}
    }
    private AlarmTask[] tasks=[];
    private int revision;
    private CancellationTokenSource? rebuildCancellation;
    public Task PendingBuild {get;private set;}=Task.CompletedTask;
    [ObservableProperty,NotifyPropertyChangedFor(nameof(MonthLabel))] private DateTime month=new(DateTime.Today.Year,DateTime.Today.Month,1);
    [ObservableProperty] private IReadOnlyList<CalendarWeek> weeks=[];
    [ObservableProperty] private CalendarEntry? selected;
    [ObservableProperty,NotifyPropertyChangedFor(nameof(DayLabel))] private DateTime selectedDay=DateTime.Today;
    [ObservableProperty] private IReadOnlyList<CalendarEntry> dayEvents=[];
    [ObservableProperty,NotifyPropertyChangedFor(nameof(HasDayRoutines))] private IReadOnlyList<CalendarEntry> dayRoutines=[];
    public bool HasDayRoutines=>DayRoutines.Count>0;
    [ObservableProperty] private string message="";
    [ObservableProperty] private bool isLoading;
    private IReadOnlyList<CalendarEntry> entries=[];
    private IReadOnlyList<CalendarEntry> routineEntries=[];
    public string MonthLabel=>Month.ToString("yyyy 年 M 月");
    public string DayLabel=>SelectedDay.ToString("M 月 d 日（ddd）",CultureInfo.GetCultureInfo("zh-TW"));
    public string DayAlmanac=>Weeks.SelectMany(w=>w.Days).FirstOrDefault(d=>d.Date==SelectedDay)?.Almanac??"農曆 "+MainViewModel.LocalLunarDate(SelectedDay);
    partial void OnMonthChanged(DateTime value){SelectedDay=value;Selected=null;PendingBuild=BuildAsync();}
    public Task SetTasksAsync(IEnumerable<AlarmTask> source){tasks=source.ToArray();return PendingBuild=BuildAsync();}
    [RelayCommand] private void PreviousMonth(){if(Month>new DateTime(1900,1,1))Month=Month.AddMonths(-1);}
    [RelayCommand] private void NextMonth(){if(Month<new DateTime(9998,12,1))Month=Month.AddMonths(1);}
    [RelayCommand] private void Today(){Month=new(DateTime.Today.Year,DateTime.Today.Month,1);SelectDay(DateTime.Today);}
    [RelayCommand] private void SelectDay(DateTime date){SelectedDay=date;Selected=null;UpdateDay();}
    [RelayCommand] private void SelectEvent(CalendarEntry entry)
    {
        Selected=entry;
        // Rebuild the day list only when the day changes, so a double-click lands on the same item.
        if(SelectedDay>=entry.Start.Date&&SelectedDay<=entry.LastDay)return;
        SelectedDay=entry.Start.Date<Month&&entry.LastDay>=Month?Month:entry.Start.Date;
        UpdateDay();
    }
    [RelayCommand] private void CloseDetails()=>Selected=null;
    private void UpdateDay()
    {
        DayEvents=entries.Where(e=>e.Start.Date<=SelectedDay&&e.LastDay>=SelectedDay).ToArray();
        DayRoutines=routineEntries.Where(e=>e.Start.Date==SelectedDay).OrderBy(e=>e.Start).ThenBy(e=>e.Title).ToArray();
        OnPropertyChanged(nameof(DayAlmanac));
    }
    private async Task BuildAsync()
    {
        rebuildCancellation?.Cancel();
        using var cancellation=new CancellationTokenSource();rebuildCancellation=cancellation;
        var current=++revision;IsLoading=true;Message="";
        try
        {
            var first=new DateTime(Month.Year,Month.Month,1);var offset=((int)first.DayOfWeek-WeekStart+7)%7;var start=first.AddDays(-offset);
            var count=(int)Math.Ceiling((offset+DateTime.DaysInMonth(first.Year,first.Month))/7d);
            var holidayRows=holidays is null?Array.Empty<Holiday>():await holidays.GetAllAsync(cancellation.Token);
            var holidayDates=holidayRows.GroupBy(h=>h.Date).ToDictionary(g=>g.Key,g=>g.First());
            var lunarRows=lunar is null?Array.Empty<LunarCalendarEntry>():await lunar.GetAllAsync(cancellation.Token);
            var solarTerms=lunarRows.Where(l=>!string.IsNullOrWhiteSpace(l.SolarTerm)).GroupBy(l=>l.Date).ToDictionary(g=>g.Key,g=>g.First().SolarTerm!);
            var end=start.AddDays(count*7);var result=new List<CalendarEntry>();var routines=new List<CalendarEntry>();
            foreach(var task in tasks)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var target=IsEverydayRoutine(task.Recurrence)?routines:result;
                if(task.ActivityStartAt is {} activity && task.ActivityEndAt is {} finish && finish>activity)
                {if(activity<end && finish>start)result.Add(new(task,activity,finish,task.ActivityAllDay));continue;}
                if(task.CalendarStartAt is {} calendar)
                {if(calendar>=start && calendar<end)result.Add(new(task,calendar,calendar,false));continue;}
                if(task.Recurrence=="None")
                {if(task.ScheduledAt>=start && task.ScheduledAt<end)result.Add(new(task,task.ScheduledAt,task.ScheduledAt,false));continue;}
                var after=start.AddTicks(-1);
                for(var i=0;i<100;i++)
                {
                    var next=holidays is not null && ReferenceEquals(target,routines)
                        ? CloudAlarmOverlay.Core.Recurrence.RecurrenceCalendar.Next(task.Recurrence,task.ScheduledAt,after,false,null,holidayRows,task.SkipOnHoliday,cancellation.Token)
                        : await scheduling.GetNextOccurrenceAsync(task with{Enabled=true},after,cancellation.Token);
                    if(next is not {} at || at>=end || at<=after)break;
                    target.Add(new(task,at,at,false));after=at;
                }
            }
            if(current!=revision)return;
            entries=result.OrderBy(e=>e.Start).ThenByDescending(e=>e.End).ThenBy(e=>e.Task.Id).ToArray();
            routineEntries=routines;
            Weeks=Enumerable.Range(0,count).Select(w=>BuildWeek(start.AddDays(w*7),first.Month,entries,holidayDates,solarTerms)).ToArray();
            if(Selected is {} old)Selected=entries.Concat(routineEntries).FirstOrDefault(e=>e.Task.Id==old.Task.Id && e.Start==old.Start);
            UpdateDay();Message=entries.Count==0?routineEntries.Count>0?"日常任務顯示於右側「當日工作」，請選擇日期查看。":"本月沒有符合條件的活動或提醒。":"跨日活動接續至下一週；日常任務請於右側查看。";
        }
        catch(OperationCanceledException){}
        catch(Exception){if(current==revision){Weeks=[];entries=[];routineEntries=[];Selected=null;DayEvents=[];DayRoutines=[];Message="月曆載入失敗，請重新整理任務。";}}
        finally{if(current==revision){IsLoading=false;rebuildCancellation=null;}}
    }
    private static bool IsEverydayRoutine(string recurrence)
    {
        if(recurrence=="Daily")return true;
        if(!recurrence.StartsWith("Weekly:",StringComparison.Ordinal))return false;
        var days=recurrence[7..].Split(',').Select(s=>int.TryParse(s,out var day)?day:0).ToHashSet();
        return days.SetEquals([1,2,3,4,5])||days.SetEquals([1,2,3,4,5,6,7]);
    }
    public static CalendarWeek BuildWeek(DateTime start,int month,IReadOnlyList<CalendarEntry> source,IReadOnlyDictionary<DateOnly,Holiday>? holidays=null,IReadOnlyDictionary<DateOnly,string>? solarTerms=null)
    {
        var finish=start.AddDays(7);var lanes=new List<int>();var segments=new List<CalendarSegment>();
        var visible=source.Where(e=>e.Start<finish && e.LastDay>=start).OrderBy(e=>e.Start).ThenByDescending(e=>e.End);
        foreach(var entry in visible)
        {
            var left=Math.Max(0,(entry.Start.Date-start).Days);var right=Math.Min(6,(entry.LastDay-start).Days);
            var lane=lanes.FindIndex(end=>end<left);if(lane<0){lane=lanes.Count;lanes.Add(right);}else lanes[lane]=right;
            if(lane<3)segments.Add(new(entry,left,right-left+1,lane,entry.Start<start,entry.LastDay>=finish));
        }
        var days=Enumerable.Range(0,7).Select(i=>start.AddDays(i)).Select(d=>new CalendarDay(d,d.Month==month,source.Where(e=>e.Start.Date<=d && e.LastDay>=d).ToArray())
            {Holiday=holidays?.GetValueOrDefault(DateOnly.FromDateTime(d)),SolarTerm=solarTerms?.GetValueOrDefault(DateOnly.FromDateTime(d))??"",HiddenCount=source.Count(e=>e.Start.Date<=d && e.LastDay>=d)-segments.Count(s=>s.Entry.Start.Date<=d && s.Entry.LastDay>=d)}).ToArray();
        return new(days,segments);
    }
}
