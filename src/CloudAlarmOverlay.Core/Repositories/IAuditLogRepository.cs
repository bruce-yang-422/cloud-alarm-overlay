using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IAuditLogRepository
{
    Task<IReadOnlyList<AuditLogEntry>> GetRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}

