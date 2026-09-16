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
    public bool IsTriggered { get; init; }
    public bool RequireAcknowledgement { get; init; }
    public string? TargetDeviceOrName { get; init; }
    public string? ExcludeDeviceOrName { get; init; }
    public string Recurrence { get; init; } = "None";
    public bool SkipOnHoliday { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
