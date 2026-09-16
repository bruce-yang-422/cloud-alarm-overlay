using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class SystemEventStore(Database db,AdminSession session):ISystemEventStore
{
    public Task AppendAsync(SystemEventEntry entry,CancellationToken ct=default)=>
        db.ExecuteAsync("INSERT INTO SystemEvents(Time,EventType,Message) VALUES(@Time,@EventType,@Message);",entry,ct);
    public Task<IReadOnlyList<SystemEventEntry>> GetRangeAsync(DateTime from,DateTime to,CancellationToken ct=default)
    {
        session.RequireAdmin();
        return db.QueryAsync<SystemEventEntry>("SELECT * FROM SystemEvents WHERE julianday(Time)>=julianday(@from) AND julianday(Time)<=julianday(@to) ORDER BY Time DESC,Id DESC;",new {from,to},ct);
    }
}
