using CloudAlarmOverlay.Core.Services;
using Dapper;
namespace CloudAlarmOverlay.Data;
public sealed class SnoozeStore(Database db) : ISnoozeStore
{
    public async Task<int> CountAsync(string id, CancellationToken ct = default) =>
        (await db.QueryAsync<int>("SELECT SnoozeCount FROM AcknowledgementLogs WHERE Id=@id;", new { id }, ct)).SingleOrDefault();
    public async Task DeferAsync(string id, DateTime until, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct); using var tx = c.BeginTransaction();
        var changed = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE AcknowledgementLogs SET SnoozeCount=SnoozeCount+1,Result='Snoozed'
            WHERE Id=@id AND AcknowledgedAt IS NULL AND SnoozeCount<3 AND Result IN ('Pending','Overdue_Unacked')
            AND EXISTS(SELECT 1 FROM Occurrences WHERE Id=@id AND State='Displayed');
            """, new { id }, tx, cancellationToken: ct));
        if (changed != 1) throw new InvalidOperationException("此通知無法再延後。");
        await c.ExecuteAsync(new CommandDefinition("UPDATE Occurrences SET State='Snoozed',SnoozedUntil=@until WHERE Id=@id;", Database.Parameters(new { id, until }), tx, cancellationToken: ct));
        tx.Commit();
    }
    public async Task MissAsync(string id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct); using var tx = c.BeginTransaction();
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE Occurrences SET State='Missed',SnoozedUntil=NULL WHERE Id=@id;
            UPDATE AcknowledgementLogs SET Result='Overdue_Unacked' WHERE Id=@id AND AcknowledgedAt IS NULL;
            """, new { id }, tx, cancellationToken: ct));
        tx.Commit();
    }
}
