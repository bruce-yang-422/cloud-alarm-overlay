using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;
namespace CloudAlarmOverlay.Data.Repositories;

internal sealed class CountdownRepository(Database db) : ICountdownRepository
{
    public Task<IReadOnlyList<CountdownItem>> GetAllAsync(CancellationToken cancellationToken = default)
        => db.QueryAsync<CountdownItem>("SELECT * FROM Countdowns ORDER BY TargetAt, Title, Id;", ct: cancellationToken);

    public async Task SaveAsync(CountdownItem item, CancellationToken cancellationToken = default)
    {
        item.Validate();
        await using var connection = await db.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred:false);
        if (item.IsPinned && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM Countdowns WHERE IsPinned=1 AND Id<>@Id;", new { item.Id }, transaction, cancellationToken:cancellationToken)) >= CountdownItem.HomePinLimit)
            throw new InvalidOperationException("首頁最多釘選 2 項，請先取消其他項目的首頁釘選。");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO Countdowns(Id,Title,TargetAt,Mode,IsPinned,CreatedAt,IsTop,Category,Repeat,ReminderDays,ReminderMinutes,Notes,CompletedAt,ReminderChangedAt,Direction,DisplayFormat,Recurrence,SkipOnHoliday)
            VALUES(@Id,@Title,@TargetAt,@Mode,@IsPinned,@CreatedAt,@IsTop,@Category,@Repeat,@ReminderDays,@ReminderMinutes,@Notes,@CompletedAt,@ReminderChangedAt,@Direction,@DisplayFormat,@Recurrence,@SkipOnHoliday)
            ON CONFLICT(Id) DO UPDATE SET Title=excluded.Title,TargetAt=excluded.TargetAt,
                Mode=excluded.Mode,IsPinned=excluded.IsPinned,IsTop=excluded.IsTop,
                Category=excluded.Category,Repeat=excluded.Repeat,ReminderDays=excluded.ReminderDays,
                ReminderMinutes=excluded.ReminderMinutes,Notes=excluded.Notes,CompletedAt=excluded.CompletedAt,
                ReminderChangedAt=excluded.ReminderChangedAt,Direction=excluded.Direction,DisplayFormat=excluded.DisplayFormat,Recurrence=excluded.Recurrence,SkipOnHoliday=excluded.SkipOnHoliday;
            """, Database.Parameters(item), transaction, cancellationToken:cancellationToken));
        transaction.Commit();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        => await db.ExecuteAsync("DELETE FROM Countdowns WHERE Id=@id;", new { id }, cancellationToken);
}
