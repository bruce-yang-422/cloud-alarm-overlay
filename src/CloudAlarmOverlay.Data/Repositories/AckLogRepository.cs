using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class AckLogRepository(Database db) : IAckLogRepository
{
    public Task<IReadOnlyList<AcknowledgementLog>> GetRangeAsync(DateTime from,DateTime to,CancellationToken cancellationToken=default)
        => db.QueryAsync<AcknowledgementLog>("SELECT * FROM AcknowledgementLogs WHERE julianday(TriggeredAt)>=julianday(@from) AND julianday(TriggeredAt)<=julianday(@to) ORDER BY TriggeredAt DESC;",new {from,to},cancellationToken);
    public Task SaveAsync(AcknowledgementLog entry,CancellationToken cancellationToken=default)
        => db.ExecuteAsync("INSERT INTO AcknowledgementLogs(Id,TaskId,DeviceId,DisplayName,TriggeredAt,AcknowledgedAt,DurationSeconds,Result,TaskName,ScheduledAt,Source) VALUES(@Id,@TaskId,@DeviceId,@DisplayName,@TriggeredAt,@AcknowledgedAt,@DurationSeconds,@Result,@TaskName,@ScheduledAt,@Source) ON CONFLICT(Id) DO UPDATE SET AcknowledgedAt=excluded.AcknowledgedAt,DurationSeconds=excluded.DurationSeconds,Result=excluded.Result;",entry,cancellationToken);
    public Task<IReadOnlyList<TaskTriggerLogEntry>> GetTriggerLogAsync(string taskId,DateTime from,DateTime to,CancellationToken cancellationToken=default)
        => db.QueryAsync<TaskTriggerLogEntry>("""
            SELECT o.Id AS OccurrenceId, o.TaskId, a.TaskName, a.Source, o.ScheduledAt,
                   o.State AS OccurrenceState, a.TriggeredAt, a.AcknowledgedAt, a.DurationSeconds, a.Result
            FROM Occurrences o
            LEFT JOIN AcknowledgementLogs a ON a.Id = o.Id
            WHERE o.TaskId=@taskId AND julianday(o.ScheduledAt)>=julianday(@from) AND julianday(o.ScheduledAt)<=julianday(@to)
            ORDER BY o.ScheduledAt DESC;
            """,new{taskId,from,to},cancellationToken);
}
