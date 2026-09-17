using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Dapper;
namespace CloudAlarmOverlay.Data;

public sealed class RuntimeStore(Database db) : IRuntimeStore
{
    public async Task<bool> ClaimAsync(string id, AlarmTask task, DateTime scheduledAt, CancellationToken ct = default)
        => await db.ExecuteAsync("INSERT OR IGNORE INTO Occurrences(Id,TaskId,ScheduledAt,State) VALUES(@id,@TaskId,@scheduledAt,'Claimed');",
            new { id, TaskId = task.Id, scheduledAt }, ct) == 1;
    public async Task DisplayedAsync(string id, AlarmTask task, DateTime scheduledAt, Device device, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        using var tx = c.BeginTransaction();
        var now = DateTime.Now;
        var args = Database.Parameters(new
        {
            id,
            TaskId = task.Id,
            TaskName = task.Title,
            task.Source,
            TaskSnapshotJson=System.Text.Json.JsonSerializer.Serialize(task),
            scheduledAt,
            device.DeviceId,
            device.DisplayName,
            now
        });
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE Occurrences SET State='Displayed',TriggeredAt=@now WHERE Id=@id;
            UPDATE Tasks SET IsTriggered=1 WHERE Id=@TaskId;
            INSERT INTO AcknowledgementLogs(Id,TaskId,TaskName,Source,ScheduledAt,DeviceId,DisplayName,TriggeredAt,Result,TaskSnapshotJson)
            VALUES(@id,@TaskId,@TaskName,@Source,@scheduledAt,@DeviceId,@DisplayName,@now,'Pending',@TaskSnapshotJson);
            """, args, tx, cancellationToken: ct));
        tx.Commit();
    }
    public async Task CompleteAsync(string id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE AcknowledgementLogs SET AcknowledgedAt=@now,
                DurationSeconds=CAST(MAX(0,(julianday(@now)-julianday(TriggeredAt))*86400) AS INTEGER),
                Result=CASE WHEN (julianday(@now)-julianday(TriggeredAt))*86400>900
                    THEN 'Overdue_Acknowledged' ELSE 'Acknowledged' END
                WHERE Id=@id AND AcknowledgedAt IS NULL;
            UPDATE Occurrences SET State='Acknowledged' WHERE Id=@id;
            """, Database.Parameters(new { id, now = DateTime.Now }), tx, cancellationToken: ct));
        tx.Commit();
    }
    public async Task MissedAsync(string id, AlarmTask task, DateTime scheduledAt, Device device, string result, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT OR IGNORE INTO AcknowledgementLogs(Id,TaskId,TaskName,Source,ScheduledAt,DeviceId,DisplayName,TriggeredAt,Result,TaskSnapshotJson)
            VALUES(@id,@TaskId,@TaskName,@Source,@scheduledAt,@DeviceId,@DisplayName,@scheduledAt,@result,@TaskSnapshotJson);
            UPDATE Occurrences SET State='Missed' WHERE Id=@id;
            """, Database.Parameters(new
        {
            id,
            TaskId = task.Id,
            TaskName = task.Title,
            task.Source,
            TaskSnapshotJson=System.Text.Json.JsonSerializer.Serialize(task),
            scheduledAt,
            device.DeviceId,
            device.DisplayName,
            result
        }), tx, cancellationToken: ct));
        tx.Commit();
    }
    public Task MarkOverdueAsync(CancellationToken ct = default)
        => db.ExecuteAsync("UPDATE AcknowledgementLogs SET Result='Overdue_Unacked' WHERE Result='Pending' AND AcknowledgedAt IS NULL AND (julianday(@now)-julianday(TriggeredAt))*86400>900;", new { now = DateTime.Now }, ct);
    public async Task RecoverAsync(CancellationToken ct = default)
    {
        // Preserve displayed-but-unacknowledged attempts; safely retry claims that never reached UI.
        await db.ExecuteAsync("""
            UPDATE AcknowledgementLogs SET Result='Overdue_Unacked' WHERE Result='Pending';
            UPDATE Occurrences SET State='Missed' WHERE State='Displayed';
            DELETE FROM Occurrences WHERE State='Claimed';
            """, ct: ct);
    }
    public async Task<bool> IsActiveAsync(string taskId, CancellationToken ct = default)
        => (await db.QueryAsync<long>("SELECT COUNT(*) FROM Occurrences WHERE TaskId=@taskId AND State IN ('Claimed','Displayed');", new { taskId }, ct)).Single() > 0;
}
