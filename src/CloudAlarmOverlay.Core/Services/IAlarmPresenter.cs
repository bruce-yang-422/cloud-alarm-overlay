using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Services;
public interface IAlarmPresenter
{
    Task ShowAsync(string occurrenceId, AlarmTask task, DateTime scheduledAt, bool preview, CancellationToken ct = default);
}
