using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class SyncLogRepository(Database db) : ISyncLogRepository
{
    public Task<IReadOnlyList<SyncLogEntry>> GetRangeAsync(DateTime from,DateTime to,CancellationToken cancellationToken=default)
        => db.QueryAsync<SyncLogEntry>("SELECT * FROM SyncLogs WHERE julianday(Time)>=julianday(@from) AND julianday(Time)<=julianday(@to) ORDER BY Time DESC;",new {from,to},cancellationToken);
    public Task AppendAsync(SyncLogEntry entry,CancellationToken cancellationToken=default)
        => db.ExecuteAsync("INSERT INTO SyncLogs(Time,Status,Message,RecordCount,Source) VALUES(@Time,@Status,@Message,@RecordCount,@Source);",entry,cancellationToken);
}
