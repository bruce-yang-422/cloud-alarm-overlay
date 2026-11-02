using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Dapper;
namespace CloudAlarmOverlay.Data;

internal sealed class PrivateSourceStore(Database db):IPrivateSourceStore
{
    public async Task ClearAsync(string source,CancellationToken ct=default,bool preserveCalendarReminders=false)
    {
        if(!source.StartsWith("Google:",StringComparison.Ordinal)||!TaskSources.IsCloud(source))throw new ArgumentException("只能清除 Google 私人來源。");
        await using var c=await db.OpenAsync(ct);using var tx=c.BeginTransaction();
        if(preserveCalendarReminders)
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO CalendarReminderRecovery(TaskId,Source,Enabled,ScheduledAt)
                SELECT Id,Source,CalendarReminderEnabled,CalendarReminderAt FROM Tasks
                WHERE Source=@source AND CalendarStartAt IS NOT NULL
                    AND (CalendarReminderEnabled IS NOT NULL OR CalendarReminderAt IS NOT NULL)
                ON CONFLICT(TaskId) DO UPDATE SET Source=excluded.Source,Enabled=excluded.Enabled,ScheduledAt=excluded.ScheduledAt;
                """,new{source},tx,cancellationToken:ct));
        else
            await c.ExecuteAsync(new CommandDefinition("DELETE FROM CalendarReminderRecovery WHERE Source=@source;",new{source},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE AcknowledgementLogs SET TaskName='已清除的私人行程',TaskSnapshotJson=NULL,Source='Google（已移除）'
                WHERE Source=@source;
            UPDATE Occurrences SET State='Missed',SnoozedUntil=NULL
                WHERE TaskId IN (SELECT Id FROM Tasks WHERE Source=@source) AND State IN ('Claimed','Displayed','Snoozed');
            DELETE FROM Tasks WHERE Source=@source;
            """,new{source},tx,cancellationToken:ct));
        tx.Commit();
    }
}
