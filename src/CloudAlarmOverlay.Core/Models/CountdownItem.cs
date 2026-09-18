using CloudAlarmOverlay.Core.Recurrence;

namespace CloudAlarmOverlay.Core.Models;

public sealed record CountdownItem
{
    public const int HomePinLimit = 2;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = "";
    public DateTime TargetAt { get; init; }
    public string Mode { get; init; } = "Days";
    public string Direction { get; init; } = "Down";
    public string DisplayFormat { get; init; } = "Days";
    public bool IsCountUp => Direction == "Up";
    public bool IsPinned { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsTop { get; init; }
    public string Category { get; init; } = "工作";
    public string Recurrence { get; init; } = "";
    public bool SkipOnHoliday { get; init; }
    public string EffectiveRecurrence => string.IsNullOrEmpty(Recurrence) ? Repeat switch
    {
        "Weekly" => "Weekly:" + (TargetAt.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)TargetAt.DayOfWeek),
        "Monthly" => "Monthly:" + TargetAt.Day, "Yearly" => $"Monthly:{TargetAt.Day}:{TargetAt.Month}", _ => "None"
    } : Recurrence;
    public string Repeat { get; init; } = "None";
    public int ReminderDays { get; init; } = -1;
    public int ReminderMinutes { get; init; } = 540;
    public string Notes { get; init; } = "";
    public DateTime? CompletedAt { get; init; }
    public DateTime ReminderChangedAt { get; init; }
    public bool IsCompleted => CompletedAt.HasValue;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Title) || Title.Length > 80)
            throw new ArgumentException("請填寫倒數名稱（最多 80 字）。");
        if (Direction is not ("Down" or "Up") || DisplayFormat is not ("Days" or "WeeksDays" or "MonthsDays" or "YearsMonthsDays"))
            throw new ArgumentException("計數方向或顯示格式無效。");
        if (Mode is not ("Days" or "Time") || TargetAt == default || TargetAt.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("倒數日期或模式無效，時間請設定到分鐘。");
        if (Mode == "Days" && TargetAt.TimeOfDay != TimeSpan.Zero)
            throw new ArgumentException("倒數日僅設定日期。");
        if (Category is not ("工作" or "生活" or "節日" or "旅行" or "其他") || Repeat is not ("None" or "Weekly" or "Monthly" or "Yearly"))
            throw new ArgumentException("倒數分類或重複設定無效。");
        if (ReminderDays is not (-1 or 0 or 1 or 3 or 7) || ReminderMinutes is < 0 or >= 1440)
            throw new ArgumentException("提醒設定無效，請設定有效的時、分。");
        if (ReminderDays == 0 && Mode == "Time" && ReminderMinutes > TargetAt.TimeOfDay.TotalMinutes)
            throw new ArgumentException("當天提醒時間不可晚於目標時間。");
        RecurrenceRule.Validate(EffectiveRecurrence);
        if (Notes is null || Notes.Length > 500) throw new ArgumentException("備註最多 500 字。");
    }

    public DateTime? NextTarget(DateTime from, IReadOnlyDictionary<DateOnly,int>? lunarDays = null, IReadOnlyList<Holiday>? holidays = null)
        => RecurrenceCalendar.Next(EffectiveRecurrence,TargetAt,from,true,lunarDays,holidays,SkipOnHoliday);

    public DateTime DisplayTarget(DateTime now, IReadOnlyDictionary<DateOnly,int>? lunarDays = null, IReadOnlyList<Holiday>? holidays = null)
        => IsCountUp ? TargetAt : NextTarget(Mode == "Days" ? (CompletedAt ?? now).Date : CompletedAt ?? now,lunarDays,holidays) ?? TargetAt;

    public DateTime? NextReminder(DateTime after, IReadOnlyDictionary<DateOnly,int>? lunarDays = null, IReadOnlyList<Holiday>? holidays = null)
    {
        if (IsCompleted || ReminderDays < 0) return null;
        var earliest = ReminderChangedAt > CreatedAt ? ReminderChangedAt : CreatedAt;
        var threshold = after > earliest ? after : earliest;
        if (threshold.Date > DateTime.MaxValue.Date.AddDays(-ReminderDays)) return null;
        var target = NextTarget(threshold.Date.AddDays(ReminderDays),lunarDays,holidays);
        while (target is {} at)
        {
            if (at.Date < DateTime.MinValue.AddDays(ReminderDays)) return null;
            var reminder = at.Date.AddDays(-ReminderDays).AddMinutes(ReminderMinutes);
            var reminderIsHoliday = SkipOnHoliday && holidays?.Any(h=>h.Date==DateOnly.FromDateTime(reminder) && h.Type!="補班日")==true;
            if (reminder > after && reminder >= earliest && !reminderIsHoliday) return reminder;
            if (at == DateTime.MaxValue) return null;
            target = NextTarget(at.AddTicks(1),lunarDays,holidays);
        }
        return null;
    }

    public AlarmTask ReminderTask(DateTime at) => new()
    {
        Id = "countdown:" + Id, Title = (IsCountUp ? "紀念日提醒 · " : "倒數提醒 · ") + Title, Level = AlarmLevels.Low,
        ScheduledAt = at, CreatedAt = CreatedAt, UpdatedAt = ReminderChangedAt,
        Description = $"分類：{Category}\n目標：{at.Date.AddDays(ReminderDays):yyyy/MM/dd}" +
            (Mode == "Time" ? $" {TargetAt:HH:mm}" : "") + (Notes.Length == 0 ? "" : "\n\n" + Notes)
    };

    public string Remaining(DateTime now, IReadOnlyDictionary<DateOnly,int>? lunarDays = null, IReadOnlyList<Holiday>? holidays = null)
    {
        if (IsCompleted) return "已完成";
        if (IsCountUp)
        {
            var at = Mode == "Days" ? now.Date : now;
            if (at < TargetAt) return "尚未開始";
            return "已過 " + FormatInterval(TargetAt, at, DisplayFormat, Mode == "Time");
        }
        if(EffectiveRecurrence!="None" && NextTarget(Mode=="Days"?now.Date:now,lunarDays,holidays) is null)
            return "尚無可用提醒日期";
        var target = DisplayTarget(now,lunarDays,holidays);
        if (DisplayFormat != "Days" && target > (Mode == "Days" ? now.Date : now))
            return "還有 " + FormatInterval(Mode == "Days" ? now.Date : now, target, DisplayFormat, Mode == "Time");
        if (Mode == "Days")
        {
            var days = (target.Date - now.Date).Days;
            return days > 0 ? $"還有 {days} 天" : days == 0 ? "就是今天" : "已到期";
        }
        var remaining = target - now;
        if (remaining <= TimeSpan.Zero) return "已到期";
        var minutes = (long)Math.Ceiling(remaining.TotalMinutes);
        return minutes >= 1440 ? $"{minutes / 1440} 天 {minutes % 1440 / 60} 時 {minutes % 60} 分"
            : minutes >= 60 ? $"{minutes / 60} 時 {minutes % 60} 分" : $"{minutes} 分鐘";
    }
    // Calendar months use the same starting anchor; a month is not approximated as 30 days.
    public static string FormatInterval(DateTime start, DateTime end, string format, bool withTime = false, bool withSeconds = false)
    {
        if (end < start) end = start;
        var span = end - start;
        string datePart;
        if (format is "MonthsDays" or "YearsMonthsDays")
        {
            var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
            if (start.AddMonths(months) > end) months--;
            months = Math.Max(0, months);
            var remainder = end - start.AddMonths(months);
            datePart = format == "MonthsDays" ? $"{months} 個月 {remainder.Days} 天"
                : $"{months / 12} 年 {months % 12} 個月 {remainder.Days} 天";
            span = remainder;
        }
        else datePart = format == "WeeksDays" ? $"{span.Days / 7} 週 {span.Days % 7} 天" : $"{span.Days} 天";
        return datePart + (withTime ? $" {span.Hours:00} 時 {span.Minutes:00} 分" + (withSeconds ? $" {span.Seconds:00} 秒" : "") : "");
    }

}
