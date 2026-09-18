using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Repositories;

public interface ICountdownRepository
{
    Task<IReadOnlyList<CountdownItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CountdownItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
