using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class TaskRepository(Database db) : ITaskRepository
{
    public Task<IReadOnlyList<AlarmTask>> GetAllAsync(CancellationToken cancellationToken = default)
        => db.QueryAsync<AlarmTask>("SELECT * FROM Tasks ORDER BY ScheduledAt;", ct: cancellationToken);
    public async Task<AlarmTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => (await db.QueryAsync<AlarmTask>("SELECT * FROM Tasks WHERE Id=@id;", new { id }, cancellationToken)).SingleOrDefault();
    public async Task SaveLocalAsync(AlarmTask task, CancellationToken cancellationToken = default)
    {
        if (task.Source != TaskSources.Local) throw new InvalidOperationException("雲端任務為唯讀，請至來源試算表修改。");
        await using var c = await db.OpenAsync(cancellationToken);
        using var tx = c.BeginTransaction();
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Tasks WHERE Id=@Id AND Source<>'本機';", task, tx) > 0)
            throw new InvalidOperationException("不可覆蓋雲端任務。");
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Occurrences WHERE TaskId=@Id AND State IN ('Claimed','Displayed','Snoozed');", task, tx) > 0)
            throw new InvalidOperationException("請先完成目前的通知再編輯任務。");
        await c.ExecuteAsync(new CommandDefinition(Upsert, Database.Parameters(task), tx, cancellationToken: cancellationToken));
        tx.Commit();
    }
    public async Task DeleteLocalAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var c = await db.OpenAsync(cancellationToken);
        using var tx = c.BeginTransaction();
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Tasks WHERE Id=@id AND Source<>'本機';", new { id }, tx) > 0)
            throw new InvalidOperationException("雲端任務為唯讀。");
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Occurrences WHERE TaskId=@id AND State IN ('Claimed','Displayed','Snoozed');", new { id }, tx) > 0)
            throw new InvalidOperationException("請先完成目前的通知再刪除任務。");
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM Tasks WHERE Id=@id AND Source='本機';", new { id }, tx, cancellationToken: cancellationToken));
        tx.Commit();
    }
    public async Task ReplaceCloudCacheAsync(string source, IReadOnlyList<AlarmTask> tasks, CancellationToken cancellationToken = default)
    {
        if (source is not (TaskSources.SheetA or TaskSources.SheetB) || tasks.Any(t => t.Source != source || string.IsNullOrWhiteSpace(t.ExternalId)))
            throw new ArgumentException("Invalid cloud source.");
        if (tasks.Select(t => t.Id).Distinct().Count() != tasks.Count) throw new ArgumentException("任務 Id 重複。");
        await using var c = await db.OpenAsync(cancellationToken);
        using var tx = c.BeginTransaction();
        foreach (var t in tasks)
        {
            var existing = await c.QuerySingleOrDefaultAsync<string>("SELECT Source FROM Tasks WHERE Id=@Id;", t, tx);
            if (existing is not null && existing != source) throw new InvalidOperationException("跨來源 Id 衝突。");
            await c.ExecuteAsync(new CommandDefinition(Upsert, Database.Parameters(t), tx, cancellationToken: cancellationToken));
        }
        var ids = tasks.Select(t => t.Id).ToArray();
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM Tasks WHERE Source=@source AND Id NOT IN @ids;", new { source, ids }, tx, cancellationToken: cancellationToken));
        tx.Commit();
    }
    private const string Upsert = """
        INSERT INTO Tasks (Id,ExternalId,Title,Description,Note,ScheduledAt,Source,Level,Enabled,IsTriggered,
            RequireAcknowledgement,TargetDeviceOrName,ExcludeDeviceOrName,Recurrence,SkipOnHoliday,CreatedAt,UpdatedAt)
        VALUES (@Id,@ExternalId,@Title,@Description,@Note,@ScheduledAt,@Source,@Level,@Enabled,@IsTriggered,
            @RequireAcknowledgement,@TargetDeviceOrName,@ExcludeDeviceOrName,@Recurrence,@SkipOnHoliday,@CreatedAt,@UpdatedAt)
        ON CONFLICT(Id) DO UPDATE SET Title=excluded.Title,Description=excluded.Description,Note=excluded.Note,
            ScheduledAt=excluded.ScheduledAt,Level=excluded.Level,Enabled=excluded.Enabled,
            RequireAcknowledgement=excluded.RequireAcknowledgement,TargetDeviceOrName=excluded.TargetDeviceOrName,
            ExcludeDeviceOrName=excluded.ExcludeDeviceOrName,Recurrence=excluded.Recurrence,
            SkipOnHoliday=excluded.SkipOnHoliday,UpdatedAt=excluded.UpdatedAt;
        """;
}
