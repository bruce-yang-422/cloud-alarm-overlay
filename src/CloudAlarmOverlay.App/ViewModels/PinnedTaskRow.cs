using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.App.ViewModels;

// Reuses the home card presentation without creating a countdown or a second reminder.
public sealed class PinnedTaskRow(AlarmTask task, DateTime? nextAt, DateTime? acknowledgedAt = null)
    : CountdownRow(new CountdownItem
    {
        Id=task.Id, Title=task.Title, TargetAt=nextAt ?? task.ScheduledAt, Mode="Time",
        CreatedAt=task.CreatedAt, IsPinned=true,
        CompletedAt=task.Recurrence=="None" ? acknowledgedAt : null
    })
{
    public override bool CanShare => false;
    public AlarmTask Task { get; } = task;
    public DateTime? NextAt { get; } = nextAt;
    public override string ModeIcon => "\uE8FD";
    public override string PinKind => "任務 · " + Task.Source;
    public override string DateCaption => NextAt is null && Task.Recurrence!="None"
        ? "截止：暫無下一次提醒" : "截止：" + TargetLabel;
    public bool NeedsReschedule(DateTime now) => Task.Enabled && Task.Recurrence!="None" && NextAt is {} at && at<=now;

    public override void Update(DateTime now, IReadOnlyDictionary<DateOnly,int>? lunarDays=null, IReadOnlyList<Holiday>? holidays=null)
    {
        base.Update(now,lunarDays,holidays);
        if (!Task.Enabled || NextAt is null && Task.Recurrence!="None")
        {
            IsScheduleUnavailable=true;
            IsExpired=false;
            HomeSummary=StatusLabel=!Task.Enabled ? "任務已停用" : "暫無下一次提醒";
            HomeElapsedLabel="";
            ShowHomeCountdown=false;
        }
        else if (Item.IsCompleted) HomeSummary=StatusLabel="已確認完成";
    }
}
