namespace CloudAlarmOverlay.Core.Models;

public static class HealthTools
{
    public static readonly string[] Kinds = ["Water", "Sitting", "Screen", "Stretch"];
    public static string Name(string kind) => kind switch { "Water" => "喝水", "Sitting" => "久坐", "Screen" => "螢幕休息", "Stretch" => "伸展", _ => throw new ArgumentException("健康工具類型無效。") };
    public static string Advice(string kind) => kind switch { "Water" => "喝點水，補充水分。", "Sitting" => "離開座位，起身活動一下。", "Screen" => "讓眼睛離開螢幕，看看遠方。", _ => "放鬆肩頸，輕輕伸展手腕與腰背。" };
    public static HealthToolOptions[] Defaults() => Kinds.Select(k => new HealthToolOptions { Kind = k, IntervalMinutes = k == "Screen" ? 20 : k == "Stretch" ? 90 : 60 }).ToArray();
}

public sealed record HealthToolOptions
{
    public string Kind { get; init; } = "Water";
    public bool Enabled { get; init; }
    public int IntervalMinutes { get; init; } = 60;
    public TimeOnly Start { get; init; } = new(8, 30);
    public TimeOnly End { get; init; } = new(18, 0);
    public int Days { get; init; } = 62; // Sunday = bit 0; Monday through Friday.
    public string DayMode { get; init; } = "Custom"; // Preserve the weekday choices in existing settings.
    public bool IsScheduledDay(DateTime date, IReadOnlyList<Holiday>? holidays = null)
    {
        if (DayMode == "Everyday") return true;
        if (DayMode != "Workdays") return (Days & (1 << (int)date.DayOfWeek)) != 0;
        var special = holidays?.Where(h => h.Date == DateOnly.FromDateTime(date)).ToArray() ?? [];
        if (special.Any(h => h.Type == "補班日")) return true;
        return special.Length == 0 && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }
    public bool IsActive(DateTime at, IReadOnlyList<Holiday>? holidays = null) => Enabled && IsScheduledDay(at, holidays) && TimeOnly.FromDateTime(at) >= Start && TimeOnly.FromDateTime(at) < End;
    public void Validate()
    {
        if (!HealthTools.Kinds.Contains(Kind) || IntervalMinutes is < 5 or > 240 || Days is < 1 or > 127 || Start >= End || DayMode is not ("Custom" or "Everyday" or "Workdays"))
            throw new ArgumentException("請設定 5–240 分鐘、同一天內的有效時段，並至少選擇一天。");
        if ((End - Start).TotalMinutes <= IntervalMinutes) throw new ArgumentException("有效時段需長於提醒間隔，才能排入至少一次提醒。");
    }
    public DateTime? NextAfter(DateTime now, DateTime? anchor = null, IReadOnlyList<Holiday>? holidays = null)
    {
        if (!Enabled) return null;
        for (var d = 0; d <= (DayMode == "Workdays" ? 366 : 7); d++)
        {
            var date = now.Date.AddDays(d);
            if (!IsScheduledDay(date, holidays)) continue;
            var start = anchor is { } aligned && aligned.Date == date ? aligned : date + Start.ToTimeSpan();
            var step = Math.Max(1, (int)Math.Floor((now - start).TotalMinutes / IntervalMinutes) + 1);
            var next = start.AddMinutes(step * IntervalMinutes);
            if (next < date + End.ToTimeSpan()) return next;
        }
        return null;
    }
}

public sealed record HealthPending(string Id, string Kind, DateTime ScheduledAt);
public sealed record HealthEvent(string Id, string Kind, DateTime ScheduledAt, DateTime RecordedAt, string Result,
    string? BatchId = null, DateTime? TriggeredAt = null, DateTime? AcknowledgedAt = null);
public sealed record HealthToolState(HealthToolOptions Options, DateTime? NextAt = null, HealthPending? Pending = null, DateTime? Anchor = null);
public sealed record HealthToolsState
{
    public HealthToolState[] Tools { get; init; } = HealthTools.Defaults().Select(o => new HealthToolState(o)).ToArray();
    public bool CoordinatePomodoro { get; init; } = true;
    public string? PomodoroMode { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveMode => PomodoroMode ?? (CoordinatePomodoro ? "Delay" : "Override");
    public string AlignmentStage { get; init; } = "None";
    public string[] AlignmentKinds { get; init; } = [];
    public string NotificationSide { get; init; } = "Right";
    public DateOnly? PausedDate { get; init; }
    public HealthEvent[] Events { get; init; } = [];
}
