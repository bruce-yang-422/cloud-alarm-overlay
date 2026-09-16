using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Services;
public interface IRuntimeStore
{
    Task<bool> ClaimAsync(string id, AlarmTask task, DateTime scheduledAt, CancellationToken ct = default);
    Task DisplayedAsync(string id, AlarmTask task, DateTime scheduledAt, Device device, CancellationToken ct = default);
    Task CompleteAsync(string id, CancellationToken ct = default);
    Task MissedAsync(string id, AlarmTask task, DateTime scheduledAt, Device device, string result, CancellationToken ct = default);
    Task MarkOverdueAsync(CancellationToken ct = default);
    Task RecoverAsync(CancellationToken ct = default);
    Task<bool> IsActiveAsync(string taskId, CancellationToken ct = default);
}
