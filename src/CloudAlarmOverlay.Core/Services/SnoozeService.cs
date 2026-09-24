using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;

public sealed record SnoozeTicket(string OccurrenceId, AlarmTask Task, DateTime ScheduledAt, DateTime Until, DateTime? NextOccurrence);
public sealed class SnoozeService(ISnoozeStore store, ITaskRepository tasks, ITaskSchedulingService schedule,
    NotificationPreferences preferences, RuntimeState runtime, ChangeSignal changes, TimeProvider clock)
{
    public async Task<SnoozeTicket?> DeferAsync(string id, AlarmTask task, DateTime scheduledAt, int minutes, CancellationToken ct=default)
    {
        if(!SnoozePolicy.IsValidMinutes(minutes) || !SnoozePolicy.CanDefer(task.Level,await store.CountAsync(id,ct),await preferences.AllowUrgentSnoozeAsync(ct)))
            throw new InvalidOperationException("此通知無法再延後。");
        var until=clock.GetLocalNow().DateTime.AddMinutes(minutes);
        var next=await schedule.GetNextOccurrenceAsync(task,scheduledAt,ct);
        await store.DeferAsync(id,until,ct);
        changes.Notify();
        if(SnoozePolicy.Overlaps(until,next))
        {
            await MissAsync(id,ct);return null;
        }
        return new(id,task,scheduledAt,until,next);
    }
    public async Task<bool> WaitAsync(SnoozeTicket ticket,CancellationToken ct=default)
    {
        while(clock.GetLocalNow().DateTime<ticket.Until)
            await Task.Delay(TimeSpan.FromSeconds(1),clock,ct);
        return await ValidateAsync(ticket,ct);
    }
    // Called again after waiting for another large notification to close.
    public async Task<bool> ValidateAsync(SnoozeTicket ticket,CancellationToken ct=default)
    {
        var now=clock.GetLocalNow().DateTime;
        var current=await tasks.GetByIdAsync(ticket.Task.Id,ct);
        var next=current is null ? null : await schedule.GetNextOccurrenceAsync(current,ticket.ScheduledAt,ct);
        if(runtime.ResumedAt>=ticket.Until || current is null || !current.Enabled ||
            current.Recurrence!=ticket.Task.Recurrence || current.ScheduledAt.TimeOfDay!=ticket.Task.ScheduledAt.TimeOfDay ||
            (ticket.Task.Recurrence=="None" && current.ScheduledAt!=ticket.ScheduledAt) ||
            (ticket.NextOccurrence is {} expected && now>=expected) || (next is {} at && now>=at))
        {
            await MissAsync(ticket.OccurrenceId,ct);return false;
        }
        return true;
    }
    private async Task MissAsync(string id,CancellationToken ct)
    {
        await store.MissAsync(id,ct);changes.Notify();
    }
}
