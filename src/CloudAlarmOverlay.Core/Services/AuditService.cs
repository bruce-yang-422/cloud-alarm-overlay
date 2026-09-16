using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class AuditService(IAuditLogRepository logs):IAuditService
{
    public Task RecordAsync(AuditLogEntry entry,CancellationToken cancellationToken=default)=>logs.AppendAsync(entry,cancellationToken);
}
