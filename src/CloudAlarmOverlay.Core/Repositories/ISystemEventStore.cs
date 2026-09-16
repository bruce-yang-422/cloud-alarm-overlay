using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Repositories;
public interface ISystemEventStore
{
    Task AppendAsync(SystemEventEntry entry,CancellationToken ct=default);
    Task<IReadOnlyList<SystemEventEntry>> GetRangeAsync(DateTime from,DateTime to,CancellationToken ct=default);
}
