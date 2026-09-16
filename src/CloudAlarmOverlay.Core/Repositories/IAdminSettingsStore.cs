using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Repositories;
public interface IAdminSettingsStore
{
    Task SaveAsync(IReadOnlyList<Setting> settings,CancellationToken ct=default);
}
