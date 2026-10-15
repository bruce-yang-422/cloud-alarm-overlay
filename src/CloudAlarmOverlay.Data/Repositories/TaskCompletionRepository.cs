using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Data.Repositories;

internal sealed class TaskCompletionRepository(Database db,ChangeSignal changes):ITaskCompletionRepository
{
    private static DateTime LocalKey(DateTime value)=>DateTime.SpecifyKind(value,DateTimeKind.Unspecified);
    public Task<IReadOnlyList<TaskCompletion>> GetAllAsync(CancellationToken ct=default)
        =>db.QueryAsync<TaskCompletion>("SELECT * FROM TaskCompletions ORDER BY CompletedAt DESC;",ct:ct);
    public async Task<bool> IsCompletedAsync(AlarmTask task,DateTime occurrence,CancellationToken ct=default)
        =>(await db.QueryAsync<int>("SELECT 1 FROM TaskCompletions WHERE Source=@Source AND TaskId=@TaskId AND OccurrenceAt=@OccurrenceAt;",
            new{task.Source,TaskId=task.Id,OccurrenceAt=LocalKey(TaskCompletion.Occurrence(task,occurrence))},ct)).Count>0;
    public async Task CompleteAsync(AlarmTask task,DateTime occurrence,CancellationToken ct=default)
    {
        // Keep stable completion keys across private-source refreshes without retaining
        // private Google titles or descriptions after that source is disconnected.
        var snapshot=task.Source.StartsWith("Google:",StringComparison.Ordinal)?task with{Title="私人來源暫不可用",Description=null,Note=null}:task;
        await db.ExecuteAsync("INSERT OR IGNORE INTO TaskCompletions(Source,TaskId,OccurrenceAt,CompletedAt,SnapshotJson) VALUES(@Source,@TaskId,@OccurrenceAt,@CompletedAt,@SnapshotJson);",
            new{task.Source,TaskId=task.Id,OccurrenceAt=LocalKey(TaskCompletion.Occurrence(task,occurrence)),CompletedAt=DateTime.Now,SnapshotJson=JsonSerializer.Serialize(snapshot)},ct);
        changes.Notify();
    }
    public async Task RestoreAsync(string source,string taskId,DateTime occurrence,CancellationToken ct=default)
    {
        await db.ExecuteAsync("DELETE FROM TaskCompletions WHERE Source=@source AND TaskId=@taskId AND OccurrenceAt=@occurrence;",new{source,taskId,occurrence=LocalKey(occurrence)},ct);changes.Notify();
    }
    public async Task DeleteSourceAsync(string source,CancellationToken ct=default)
    {
        await db.ExecuteAsync("DELETE FROM TaskCompletions WHERE Source=@source;",new{source},ct);changes.Notify();
    }
}
