namespace CloudAlarmOverlay.Core.Repositories;

public interface ITaskHomePinRepository
{
    Task<int> GetLimitAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetTaskIdsAsync(CancellationToken cancellationToken = default);
    Task SetPinnedAsync(string taskId, bool pinned, CancellationToken cancellationToken = default);
}
