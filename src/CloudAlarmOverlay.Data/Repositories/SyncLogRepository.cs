using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class SyncLogRepository(Database db) : ISyncLogRepository
{
    public Task<IReadOnlyList<SyncState>> GetStatesAsync(CancellationToken cancellationToken=default)
        =>db.QueryAsync<SyncState>("SELECT * FROM SyncStates;",ct:cancellationToken);
    public Task<int> PruneLegacySuccessAsync(DateTime before,CancellationToken cancellationToken=default)
        =>db.ExecuteAsync("DELETE FROM SyncLogs WHERE EventKind='Legacy' AND Status='成功' AND julianday(Time)<julianday(@before);",new{before},cancellationToken);
    public async Task RecordAsync(SyncLogEntry entry,string? fingerprint,string configFingerprint,CancellationToken cancellationToken=default)
    {
        await using var c=await db.OpenAsync(cancellationToken);
        using var tx=c.BeginTransaction();
        var previous=await c.QuerySingleOrDefaultAsync<SyncState>(new CommandDefinition(
            "SELECT * FROM SyncStates WHERE Source=@Source;",new{entry.Source},tx,cancellationToken:cancellationToken));
        var success=entry.Status=="成功";
        var sameConfig=previous?.ConfigFingerprint==configFingerprint;
        var unchanged=previous is not null && sameConfig && previous.Status==entry.Status &&
            (success?previous.Fingerprint==fingerprint:previous.Message==entry.Message);
        var logId=previous?.ActiveLogId;
        if(unchanged && !success && logId is not null)
            await c.ExecuteAsync(new CommandDefinition("UPDATE SyncLogs SET LastSeenAt=@Time,RepeatCount=RepeatCount+1 WHERE Id=@Id;",
                Database.Parameters(new{entry.Time,Id=logId}),tx,cancellationToken:cancellationToken));
        else if(!unchanged)
        {
            var kind=!success?"Failure":previous is null?"Initial":!sameConfig?"Configuration":previous.Status!="成功"?"Recovery":"Changed";
            var prefix=kind switch {"Initial"=>"首次同步。", "Configuration"=>"同步設定已變更。", "Recovery"=>"連線已恢復。", "Changed"=>"資料已異動。", _=>""};
            logId=await c.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT INTO SyncLogs(Time,Status,Message,RecordCount,Source,LastSeenAt,RepeatCount,EventKind)
                VALUES(@Time,@Status,@Message,@RecordCount,@Source,@Time,1,@EventKind);
                SELECT last_insert_rowid();
                """,Database.Parameters(entry with{EventKind=kind,Message=prefix+entry.Message}),tx,cancellationToken:cancellationToken));
        }
        var state=new SyncState{Source=entry.Source!,Fingerprint=success?fingerprint:previous?.Fingerprint,
            ConfigFingerprint=configFingerprint,Status=entry.Status,Message=entry.Message,LastCheckedAt=entry.Time,
            LastSuccessAt=success?entry.Time:sameConfig?previous?.LastSuccessAt:null,ActiveLogId=logId};
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO SyncStates(Source,Fingerprint,ConfigFingerprint,Status,Message,LastCheckedAt,LastSuccessAt,ActiveLogId)
            VALUES(@Source,@Fingerprint,@ConfigFingerprint,@Status,@Message,@LastCheckedAt,@LastSuccessAt,@ActiveLogId)
            ON CONFLICT(Source) DO UPDATE SET Fingerprint=excluded.Fingerprint,ConfigFingerprint=excluded.ConfigFingerprint,
                Status=excluded.Status,Message=excluded.Message,LastCheckedAt=excluded.LastCheckedAt,
                LastSuccessAt=excluded.LastSuccessAt,ActiveLogId=excluded.ActiveLogId;
            """,Database.Parameters(state),tx,cancellationToken:cancellationToken));
        tx.Commit();
    }
    public Task<IReadOnlyList<SyncLogEntry>> GetRangeAsync(DateTime from,DateTime to,CancellationToken cancellationToken=default)
        => db.QueryAsync<SyncLogEntry>("SELECT * FROM SyncLogs WHERE julianday(COALESCE(LastSeenAt,Time))>=julianday(@from) AND julianday(Time)<=julianday(@to) ORDER BY Time DESC;",new {from,to},cancellationToken);
    public Task AppendAsync(SyncLogEntry entry,CancellationToken cancellationToken=default)
        => db.ExecuteAsync("INSERT INTO SyncLogs(Time,Status,Message,RecordCount,Source) VALUES(@Time,@Status,@Message,@RecordCount,@Source);",entry,cancellationToken);
}
