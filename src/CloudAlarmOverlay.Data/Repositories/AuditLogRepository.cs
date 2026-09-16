using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class AuditLogRepository(Database db,AdminSession session):IAuditLogRepository
{
    public Task<IReadOnlyList<AuditLogEntry>> GetRangeAsync(DateTime from,DateTime to,CancellationToken cancellationToken=default)
    {
        session.RequireAdmin();
        return db.QueryAsync<AuditLogEntry>("SELECT * FROM AuditLogs WHERE julianday(CreatedAt)>=julianday(@from) AND julianday(CreatedAt)<=julianday(@to) ORDER BY CreatedAt DESC,Id DESC;",new {from,to},cancellationToken);
    }
    public Task AppendAsync(AuditLogEntry entry,CancellationToken cancellationToken=default)=>
        db.ExecuteAsync("INSERT INTO AuditLogs(UserId,Action,OldValue,NewValue,CreatedAt) VALUES(@UserId,@Action,@OldValue,@NewValue,@CreatedAt);",entry,cancellationToken);
}
