namespace CloudAlarmOverlay.Core.Models;

public sealed record AlarmTask
{
    public required string Id { get; init; }
    public string? ExternalId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Note { get; init; }
    public required DateTime ScheduledAt { get; init; }
    public string Source { get; init; } = TaskSources.Local;
    public string Level { get; init; } = AlarmLevels.Mid;
    public bool Enabled { get; init; } = true;
    public DateTime? ActivityStartAt { get; init; }
    // Exclusive end; an all-day October 1–10 range is stored as October 1–11.
    public DateTime? ActivityEndAt { get; init; }
    public bool ActivityAllDay { get; init; }
    public DateTime? CalendarStartAt { get; init; }
    public bool? GoogleReminderEnabled { get; init; }
    public DateTime? GoogleReminderAt { get; init; }
    public bool? CalendarReminderEnabled { get; init; }
    public DateTime? CalendarReminderAt { get; init; }
    public bool IsGoogleCalendar => Source.StartsWith("Google:",StringComparison.Ordinal) && CalendarStartAt.HasValue && GoogleReminderAt.HasValue && GoogleReminderEnabled.HasValue;
    public bool HasCalendarReminderOverride => IsGoogleCalendar && (CalendarReminderEnabled.HasValue || CalendarReminderAt.HasValue);
    public bool IsTriggered { get; init; }
    public bool RequireAcknowledgement { get; init; }
    public string? TargetDeviceOrName { get; init; }
    public string? ExcludeDeviceOrName { get; init; }
    public string Recurrence { get; init; } = "None";
    public bool SkipOnHoliday { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
