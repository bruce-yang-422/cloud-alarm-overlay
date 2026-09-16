using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IAuditService
{
    Task RecordAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}

