using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Dapper;
namespace CloudAlarmOverlay.Data;

internal sealed class PrivateSourceStore(Database db):IPrivateSourceStore
{
    public async Task ClearAsync(string source,CancellationToken ct=default)
    {
        if(!source.StartsWith("Google:",StringComparison.Ordinal)||!TaskSources.IsCloud(source))throw new ArgumentException("只能清除 Google 私人來源。");
        await using var c=await db.OpenAsync(ct);using var tx=c.BeginTransaction();
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE AcknowledgementLogs SET TaskName='已清除的私人行程',TaskSnapshotJson=NULL,Source='Google（已移除）'
                WHERE Source=@source;
            UPDATE Occurrences SET State='Missed',SnoozedUntil=NULL
                WHERE TaskId IN (SELECT Id FROM Tasks WHERE Source=@source) AND State IN ('Claimed','Displayed','Snoozed');
            DELETE FROM Tasks WHERE Source=@source;
            """,new{source},tx,cancellationToken:ct));
        tx.Commit();
    }
}
