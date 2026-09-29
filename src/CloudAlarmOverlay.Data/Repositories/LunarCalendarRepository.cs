using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class LunarCalendarRepository(Database db,ICalendarDataStore calendar) : ILunarCalendarRepository
{
    public async Task<IReadOnlyList<LunarCalendarEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sheet=await db.QueryAsync<LunarCalendarEntry>("SELECT * FROM LunarCalendar ORDER BY Date;",ct:cancellationToken);
        var baseline=await calendar.ReadAsync(cancellationToken);
        return sheet.Concat(baseline.Lunar?.Entries??[]).GroupBy(l=>l.Date).Select(g=>g.First()).OrderBy(l=>l.Date).ToArray();
    }
    public async Task<LunarCalendarEntry?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        => (await db.QueryAsync<LunarCalendarEntry>("SELECT * FROM LunarCalendar WHERE Date=@date;",new {date},cancellationToken)).SingleOrDefault()
            ?? (await calendar.ReadAsync(cancellationToken)).Lunar?.Entries.FirstOrDefault(l=>l.Date==date);
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
