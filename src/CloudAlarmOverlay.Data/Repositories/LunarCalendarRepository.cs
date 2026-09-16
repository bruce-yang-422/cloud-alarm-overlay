using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class LunarCalendarRepository(Database db) : ILunarCalendarRepository
{
    public Task<IReadOnlyList<LunarCalendarEntry>> GetAllAsync(CancellationToken cancellationToken = default)
        => db.QueryAsync<LunarCalendarEntry>("SELECT * FROM LunarCalendar ORDER BY Date;",ct:cancellationToken);
    public async Task<LunarCalendarEntry?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        => (await db.QueryAsync<LunarCalendarEntry>("SELECT * FROM LunarCalendar WHERE Date=@date;",new {date},cancellationToken)).SingleOrDefault();
    public async Task ReplaceCacheAsync(IReadOnlyList<LunarCalendarEntry> entries, CancellationToken cancellationToken = default)
    {
        await using var c = await db.OpenAsync(cancellationToken);
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM LunarCalendar;", transaction:tx, cancellationToken:cancellationToken));
        foreach(var row in entries)
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO LunarCalendar(Date,LunarDate,LunarDay,SolarTerm) VALUES(@Date,@LunarDate,@LunarDay,@SolarTerm);",
                Database.Parameters(row),tx,cancellationToken:cancellationToken));
        tx.Commit();
    }
}
