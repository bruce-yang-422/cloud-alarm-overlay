using System.Globalization;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace CloudAlarmOverlay.BackgroundServices;

public sealed class AlarmWorker(ITaskRepository tasks, ITaskSchedulingService schedule, IRuntimeStore runtime,
    IDeviceIdentityService identity, IAlarmService alarms, ISettingsRepository settings, ChangeSignal signal,
    RuntimeState state, IAlarmHeartbeat heartbeat, ILogger<AlarmWorker> logger, ICountdownRepository countdowns, TimeProvider clock, IHolidayRepository holidays) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = new List<Task>();
        try {await runtime.RecoverAsync(stoppingToken);}
        catch(Exception ex){heartbeat.Fail(ex.Message);throw;}
        var previous = (await settings.GetAsync("AlarmCheckpoint", stoppingToken))?.Value;
        var cursor = DateTime.TryParse(previous, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var saved) ? saved : clock.GetLocalNow().DateTime;
        bool startup = true;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = clock.GetLocalNow().DateTime;
                    var device = await identity.GetLocalAsync(stoppingToken);
                    DateTime next = now.AddMinutes(1);
                    if (device is not null)
                    {
                        foreach (var task in await tasks.GetAllAsync(stoppingToken))
                        {
                            // Do not backfill occurrences before this task was first observed locally.
                            var after = cursor > task.CreatedAt ? cursor : task.CreatedAt.AddTicks(-1);
                            var occurrence = await schedule.GetNextOccurrenceAsync(task, after, stoppingToken);
                            while (occurrence is { } at && at <= now)
                            {
                                var id = OccurrenceIdentity.For(task.Id, at);
                                if (await runtime.ClaimAsync(id, task, at, stoppingToken))
                                {
                                    if (startup || (state.ResumedAt > cursor && at < state.ResumedAt))
                                        await runtime.MissedAsync(id, task, at, device, startup ? "NotLaunched" : "Overdue_Unacked", stoppingToken);
                                    else pending.Add(DispatchAsync(task with { ScheduledAt = at }, stoppingToken));
                                }
                                occurrence = await schedule.GetNextOccurrenceAsync(task, at, stoppingToken);
                            }
                            if (occurrence is { } future && future < next) next = future;
                        }
                        var holidayList=await holidays.GetAllAsync(stoppingToken);
                        foreach (var item in await countdowns.GetAllAsync(stoppingToken))
                        {
                            var occurrence = item.NextReminder(cursor,null,holidayList);
                            while (occurrence is {} at && at <= now)
                            {
                                var task = item.ReminderTask(at);
                                var id = OccurrenceIdentity.For(task.Id, at);
                                if (await runtime.ClaimAsync(id, task, at, stoppingToken))
                                {
                                    if (startup || (state.ResumedAt > cursor && at < state.ResumedAt))
                                        await runtime.MissedAsync(id, task, at, device, startup ? "NotLaunched" : "Overdue_Unacked", stoppingToken);
                                    else pending.Add(DispatchAsync(task, stoppingToken));
                                }
                                occurrence = item.NextReminder(at,null,holidayList);
                            }
                            if (occurrence is {} future && future < next) next = future;
                        }
                        await runtime.MarkOverdueAsync(stoppingToken);
                        await settings.SaveAsync(new Setting { Key = "AlarmCheckpoint", Value = now.ToString("O", CultureInfo.InvariantCulture) }, stoppingToken);
                    }
                    pending.RemoveAll(t => t.IsCompleted);
                    cursor = now; startup = false;
                    heartbeat.Tick();
                    var delay = next - clock.GetLocalNow().DateTime;
                    await signal.WaitAsync(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    heartbeat.Fail(ex.Message);
                    logger.LogError(ex, "排程計算失敗，30 秒後重試");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }
        finally { heartbeat.Stop(); await Task.WhenAll(pending); }
    }
    private async Task DispatchAsync(AlarmTask task, CancellationToken ct)
    {
        try { await alarms.EnqueueAsync(task, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "提醒顯示失敗：{TaskId}", task.Id);
            try
            {
                if (await identity.GetLocalAsync(ct) is { } device)
                    await runtime.MissedAsync(OccurrenceIdentity.For(task.Id, task.ScheduledAt), task, task.ScheduledAt, device, "Overdue_Unacked", ct);
            }
            catch (Exception recoveryError) { logger.LogError(recoveryError, "無法記錄提醒失敗：{TaskId}", task.Id); }
        }
    }
}
