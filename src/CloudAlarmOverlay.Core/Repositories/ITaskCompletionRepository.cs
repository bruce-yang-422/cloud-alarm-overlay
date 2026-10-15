using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Repositories;

public interface ITaskCompletionRepository
{
    Task<IReadOnlyList<TaskCompletion>> GetAllAsync(CancellationToken ct=default);
    Task<bool> IsCompletedAsync(AlarmTask task,DateTime occurrence,CancellationToken ct=default);
    Task CompleteAsync(AlarmTask task,DateTime occurrence,CancellationToken ct=default);
    Task RestoreAsync(string source,string taskId,DateTime occurrence,CancellationToken ct=default);
    Task DeleteSourceAsync(string source,CancellationToken ct=default);
}
