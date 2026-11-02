using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Recurrence;
using CloudAlarmOverlay.Core.Repositories;

namespace CloudAlarmOverlay.Core.Services;

public sealed class CalendarIcsExporter(ITaskRepository tasks,ICountdownRepository countdowns,IHolidayRepository holidays,
    ITaskCompletionRepository completions,ICalendarDataStore calendar,TimeProvider clock):ICalendarIcsExporter
{
    public const int MaximumEvents=50000;
    private sealed record Event(string Kind,string Id,DateTime Occurrence,string Title,string Description,string Category,
        DateTime Start,DateTime End,bool AllDay,DateTime? Reminder);

    public async Task<CalendarExportResult> ExportAsync(CalendarExportOptions options,CancellationToken ct=default)
    {
        options.Validate();
        var taskRows=options.IncludeTasks?await tasks.GetAllAsync(ct):[];
        var countdownRows=options.IncludeCountdowns?await countdowns.GetAllAsync(ct):[];
        var holidayRows=await holidays.GetAllAsync(ct);
        var completed=options.IncludeTasks?await completions.GetAllAsync(ct):[];
        var state=await calendar.ReadAsync(ct);
        // Expansion and UTF-8 folding may cover many years; keep them off the UI thread.
        return await Task.Run(()=>Build(options,taskRows,countdownRows,holidayRows,completed,state,clock.GetUtcNow(),ct),ct);
    }
    private static CalendarExportResult Build(CalendarExportOptions options,IReadOnlyList<AlarmTask> tasks,
        IReadOnlyList<CountdownItem> countdowns,IReadOnlyList<Holiday> holidays,IReadOnlyList<TaskCompletion> completions,
        CalendarDataState state,DateTimeOffset now,CancellationToken ct)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var holidayDates=holidays.GroupBy(h=>h.Date).ToDictionary(g=>g.Key,g=>g.ToArray());
        var completed=completions.Where(c=>c.Source==TaskSources.Local).Select(c=>(c.TaskId,c.OccurrenceAt)).ToHashSet();
        var events=new List<Event>();var warnings=new HashSet<string>();var taskCount=0;var countdownCount=0;
        void Add(Event item)
        {
            if(events.Count>=MaximumEvents)throw new InvalidOperationException("超過 50,000 個事件，請縮小日期範圍或分來源匯出。");
            events.Add(item);
        }
        void ValidateCalendar(string rule,bool skipHoliday)
        {
            RecurrenceRule.Validate(rule);
            if(rule.StartsWith("Lunar",StringComparison.Ordinal))
            {
                var lunar=new ChineseLunisolarCalendar();
                if(options.From.ToDateTime(TimeOnly.MinValue)<lunar.MinSupportedDateTime||options.To.ToDateTime(TimeOnly.MinValue)>lunar.MaxSupportedDateTime)
                    throw new ArgumentException($"農曆規則的匯出期間限 {lunar.MinSupportedDateTime:yyyy/MM/dd}–{lunar.MaxSupportedDateTime:yyyy/MM/dd}，請調整日期範圍。");
            }
            if(skipHoliday||rule.StartsWith("Weekly:",StringComparison.Ordinal))
            {
                if(state.Holidays is not {} coverage)
                    warnings.Add("目前沒有完整的假日涵蓋期間資訊；略過假日及補班日只依本機已知資料展開，可能需要在日曆中手動調整。");
                else if(options.From<coverage.From||options.To>coverage.To)
                    warnings.Add($"假日資料涵蓋 {coverage.From:yyyy/MM/dd}–{coverage.To:yyyy/MM/dd}；範圍外只依本機已知假日／補班日展開，未來公告變更不會自動更新至匯出檔。");
            }
        }
        bool Matches(string rule,DateTime anchor,DateOnly date,bool skipHoliday)
        {
            var match=RecurrenceRule.Matches(rule,date,anchor);
            if(holidayDates.TryGetValue(date,out var entries))
            {
                if(rule.StartsWith("Weekly:",StringComparison.Ordinal)&&entries.Any(h=>h.Type=="補班日"))match=true;
                if(skipHoliday&&entries.Any(h=>h.Type!="補班日"))match=false;
            }
            return match&&date>=DateOnly.FromDateTime(anchor);
        }
        IEnumerable<DateTime> Occurrences(string rule,DateTime anchor,bool skipHoliday)
        {
            ValidateCalendar(rule,skipHoliday);
            var start=DateOnly.FromDateTime(anchor)>options.From?DateOnly.FromDateTime(anchor):options.From;
            if(rule=="None")
            {
                var date=DateOnly.FromDateTime(anchor);
                if(date>=options.From&&date<=options.To&&Matches(rule,anchor,date,skipHoliday))yield return anchor;
                yield break;
            }
            for(var date=start;date<=options.To;date=date.AddDays(1))
            {
                ct.ThrowIfCancellationRequested();
                if(Matches(rule,anchor,date,skipHoliday))yield return date.ToDateTime(TimeOnly.FromDateTime(anchor));
            }
        }
        foreach(var task in tasks.Where(t=>t.Source==TaskSources.Local&&(t.Enabled||options.IncludeDisabledTasks)))
        {
            ct.ThrowIfCancellationRequested();ActivityPeriod.Validate(task);
            var hasActivity=task.ActivityStartAt.HasValue;
            // Period activities are one-off. Their calendar range follows the activity,
            // while completion and reminders still use the original scheduled occurrence.
            var occurrences=hasActivity?new[]{task.ScheduledAt}:Occurrences(task.Recurrence,task.ScheduledAt,task.SkipOnHoliday);
            foreach(var occurrence in occurrences)
            {
                var done=completed.Contains((task.Id,occurrence));if(done&&!options.IncludeCompleted)continue;
                var start=task.ActivityStartAt??occurrence;
                if(DateOnly.FromDateTime(start)<options.From||DateOnly.FromDateTime(start)>options.To)continue;
                if(hasActivity)
                {
                    ValidateCalendar(task.Recurrence,task.SkipOnHoliday);
                    if(!Matches(task.Recurrence,task.ScheduledAt,DateOnly.FromDateTime(occurrence),task.SkipOnHoliday))continue;
                }
                var description=string.Join("\n\n",new[]{task.Description,task.Note,done?"本次任務已完成":null}.Where(s=>!string.IsNullOrEmpty(s)));
                Add(new("task",task.Id,occurrence,task.Title,description,"本機任務",start,task.ActivityEndAt??start.AddMinutes(30),task.ActivityAllDay,
                    options.IncludeReminders&&task.Enabled&&!done?occurrence:null));taskCount++;
            }
        }
        foreach(var item in countdowns.Where(c=>!c.IsCompleted||options.IncludeCompleted))
        {
            ct.ThrowIfCancellationRequested();item.Validate();
            var anchor=item.IsCompleted?item.DisplayTarget(item.CompletedAt!.Value,holidays:holidays):item.TargetAt;
            var rule=item.IsCompleted?"None":item.EffectiveRecurrence;
            foreach(var at in Occurrences(rule,anchor,item.SkipOnHoliday))
            {
                DateTime? reminder=null;
                if(options.IncludeReminders&&!item.IsCompleted&&item.ReminderDays>=0)
                {
                    var candidate=at.Date.AddDays(-item.ReminderDays).AddMinutes(item.ReminderMinutes);
                    if(!item.SkipOnHoliday||!holidayDates.TryGetValue(DateOnly.FromDateTime(candidate),out var entries)||entries.All(h=>h.Type=="補班日"))reminder=candidate;
                }
                var description=item.Notes+(item.IsCompleted?"\n\n此事件已完成":"");
                Add(new("countdown",item.Id,at,item.Title,description,$"{(item.IsCountUp?"正數事件":"倒數事件")},{item.Category}",at,
                    item.Mode=="Days"?at.Date.AddDays(1):at.AddMinutes(30),item.Mode=="Days",reminder));countdownCount++;
            }
        }
        var content=new StringBuilder();
        void Line(string value)
        {
            // RFC 5545 folds at 75 UTF-8 octets, never through a multibyte rune.
            var octets=0;
            foreach(var rune in value.EnumerateRunes())
            {
                if(octets+rune.Utf8SequenceLength>75){content.Append("\r\n ");octets=1;}
                content.Append(rune.ToString());octets+=rune.Utf8SequenceLength;
            }
            content.Append("\r\n");
        }
        static string Escape(string value)=>new string(value.Where(c=>c>=' '||c is '\r' or '\n' or '\t').ToArray())
            .Replace("\r\n","\n").Replace('\r','\n').Replace("\\","\\\\").Replace(";","\\;").Replace(",","\\,").Replace("\n","\\n");
        string Utc(DateTime value)
        {
            var local=DateTime.SpecifyKind(value,DateTimeKind.Unspecified);
            if(zone.IsInvalidTime(local))throw new ArgumentException($"{value:yyyy/MM/dd HH:mm} 在時區 {zone.Id} 中不存在，請調整該事件時間後再匯出。");
            return TimeZoneInfo.ConvertTimeToUtc(local,zone).ToString("yyyyMMdd'T'HHmmss'Z'",CultureInfo.InvariantCulture);
        }
        Line("BEGIN:VCALENDAR");Line("VERSION:2.0");Line("PRODID:-//Cloud Alarm Overlay//Calendar Export//ZH-TW");Line("CALSCALE:GREGORIAN");
        foreach(var item in events.OrderBy(e=>e.Start).ThenBy(e=>e.Id,StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var identity=item.Kind+"|"+item.Id+"|"+item.Occurrence.ToString("yyyyMMddTHHmmssfffffff",CultureInfo.InvariantCulture);
            Line("BEGIN:VEVENT");Line("UID:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()+"@cloud-alarm-overlay");
            Line("DTSTAMP:"+now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'",CultureInfo.InvariantCulture));
            Line(item.AllDay?"DTSTART;VALUE=DATE:"+item.Start.ToString("yyyyMMdd",CultureInfo.InvariantCulture):"DTSTART:"+Utc(item.Start));
            Line(item.AllDay?"DTEND;VALUE=DATE:"+item.End.ToString("yyyyMMdd",CultureInfo.InvariantCulture):"DTEND:"+Utc(item.End));
            Line("SUMMARY:"+Escape(item.Title));
            if(item.Description.Length>0)Line("DESCRIPTION:"+Escape(item.Description));
            Line("CATEGORIES:"+string.Join(',',item.Category.Split(',').Select(Escape)));Line("CLASS:PRIVATE");Line("TRANSP:TRANSPARENT");
            if(item.Reminder is {} reminder)
            {
                Line("BEGIN:VALARM");Line("ACTION:DISPLAY");Line("DESCRIPTION:"+Escape(item.Title));
                Line("TRIGGER;VALUE=DATE-TIME:"+Utc(reminder));Line("END:VALARM");
            }
            Line("END:VEVENT");
        }
        Line("END:VCALENDAR");
        return new(content.ToString(),taskCount,countdownCount,warnings.Order(StringComparer.Ordinal).ToArray());
    }
}
