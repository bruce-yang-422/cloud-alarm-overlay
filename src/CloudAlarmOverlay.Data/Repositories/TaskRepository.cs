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
        ActivityPeriod.Validate(task);
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Tasks WHERE Id=@Id AND Source<>'本機';", task, tx) > 0)
            throw new InvalidOperationException("不可覆蓋雲端任務。");
        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Occurrences WHERE TaskId=@Id AND State IN ('Claimed','Displayed','Snoozed');", task, tx) > 0)
            throw new InvalidOperationException("請先完成目前的通知再編輯任務。");
        await c.ExecuteAsync(new CommandDefinition(Upsert, Database.Parameters(task with {CalendarStartAt=null,GoogleReminderEnabled=null,GoogleReminderAt=null,CalendarReminderEnabled=null,CalendarReminderAt=null}), tx, cancellationToken: cancellationToken));
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
        if (!TaskSources.IsCloud(source) || tasks.Any(t => t.Source != source || string.IsNullOrWhiteSpace(t.ExternalId)))
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
    public Task SaveCalendarReminderAsync(string id,bool enabled,DateTime? scheduledAt,CancellationToken cancellationToken=default)
        =>ChangeCalendarReminderAsync(id,enabled,scheduledAt,false,cancellationToken);
    public Task ResetCalendarReminderAsync(string id,CancellationToken cancellationToken=default)
        =>ChangeCalendarReminderAsync(id,false,null,true,cancellationToken);
    private async Task ChangeCalendarReminderAsync(string id,bool enabled,DateTime? at,bool reset,CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct);
        using var tx=c.BeginTransaction();
        var current=await c.QuerySingleOrDefaultAsync<AlarmTask>(new CommandDefinition("SELECT * FROM Tasks WHERE Id=@id;",new{id},tx,cancellationToken:ct));
        if(current?.IsGoogleCalendar!=true)throw new InvalidOperationException("只有已同步的 Google 日曆行程可調整本機提醒，請先同步日曆。");
        if(await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Occurrences WHERE TaskId=@id AND State IN ('Claimed','Displayed','Snoozed');",new{id},tx,cancellationToken:ct))>0)
            throw new InvalidOperationException("請先完成目前的通知，再修改提醒設定。");
        var localTime=reset?null:at??current.CalendarReminderAt;
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE Tasks SET CalendarReminderEnabled=@localEnabled,CalendarReminderAt=@localTime,
                Enabled=COALESCE(@localEnabled,GoogleReminderEnabled),ScheduledAt=COALESCE(@localTime,GoogleReminderAt),UpdatedAt=@updated
            WHERE Id=@id;
            """,Database.Parameters(new{id,localEnabled=reset?(bool?)null:enabled,localTime,updated=DateTime.Now}),tx,cancellationToken:ct));
        tx.Commit();
    }
    private const string Upsert = """
        INSERT INTO Tasks (Id,ExternalId,Title,Description,Note,ScheduledAt,Source,Level,Enabled,IsTriggered,
            RequireAcknowledgement,TargetDeviceOrName,ExcludeDeviceOrName,Recurrence,SkipOnHoliday,CreatedAt,UpdatedAt,CalendarStartAt,GoogleReminderEnabled,GoogleReminderAt,ActivityStartAt,ActivityEndAt,ActivityAllDay)
        VALUES (@Id,@ExternalId,@Title,@Description,@Note,@ScheduledAt,@Source,@Level,@Enabled,@IsTriggered,
            @RequireAcknowledgement,@TargetDeviceOrName,@ExcludeDeviceOrName,@Recurrence,@SkipOnHoliday,@CreatedAt,@UpdatedAt,@CalendarStartAt,@GoogleReminderEnabled,@GoogleReminderAt,@ActivityStartAt,@ActivityEndAt,@ActivityAllDay)
        ON CONFLICT(Id) DO UPDATE SET ActivityStartAt=excluded.ActivityStartAt,ActivityEndAt=excluded.ActivityEndAt,ActivityAllDay=excluded.ActivityAllDay,Title=excluded.Title,Description=excluded.Description,Note=excluded.Note,
            ScheduledAt=CASE WHEN excluded.CalendarStartAt IS NOT NULL THEN COALESCE(Tasks.CalendarReminderAt,excluded.ScheduledAt) ELSE excluded.ScheduledAt END,
            Level=excluded.Level,Enabled=CASE WHEN excluded.CalendarStartAt IS NOT NULL THEN COALESCE(Tasks.CalendarReminderEnabled,excluded.Enabled) ELSE excluded.Enabled END,
            CalendarStartAt=excluded.CalendarStartAt,GoogleReminderEnabled=excluded.GoogleReminderEnabled,GoogleReminderAt=excluded.GoogleReminderAt,
            CalendarReminderEnabled=CASE WHEN excluded.CalendarStartAt IS NOT NULL THEN Tasks.CalendarReminderEnabled ELSE NULL END,
            CalendarReminderAt=CASE WHEN excluded.CalendarStartAt IS NOT NULL THEN Tasks.CalendarReminderAt ELSE NULL END,
            RequireAcknowledgement=excluded.RequireAcknowledgement,TargetDeviceOrName=excluded.TargetDeviceOrName,
            ExcludeDeviceOrName=excluded.ExcludeDeviceOrName,Recurrence=excluded.Recurrence,
            SkipOnHoliday=excluded.SkipOnHoliday,UpdatedAt=excluded.UpdatedAt;
        """;
}
