using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ISyncLogRepository
{
    Task<IReadOnlyList<SyncLogEntry>> GetRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task AppendAsync(SyncLogEntry entry, CancellationToken cancellationToken = default);
}

