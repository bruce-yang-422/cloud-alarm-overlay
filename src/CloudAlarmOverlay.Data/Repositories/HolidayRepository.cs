using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class HolidayRepository(Database db) : IHolidayRepository
{
    public Task<IReadOnlyList<Holiday>> GetAllAsync(CancellationToken cancellationToken = default)
        => db.QueryAsync<Holiday>("SELECT * FROM Holidays;",ct:cancellationToken);
    public async Task ReplaceCacheAsync(IReadOnlyList<Holiday> holidays, CancellationToken cancellationToken = default)
    {
        await using var c = await db.OpenAsync(cancellationToken);
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM Holidays;", transaction:tx, cancellationToken:cancellationToken));
        foreach(var row in holidays)
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO Holidays(Date,Type,Note,Source) VALUES(@Date,@Type,@Note,@Source);",
                Database.Parameters(row),tx,cancellationToken:cancellationToken));
        tx.Commit();
    }
}
