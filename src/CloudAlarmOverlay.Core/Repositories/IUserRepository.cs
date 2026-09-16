using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Repositories;
public interface IUserRepository
{
    Task<bool> AnyAsync(CancellationToken ct=default);
    Task CreateInitialAsync(User user,CancellationToken ct=default);
    Task<User?> GetByUsernameAsync(string username,CancellationToken cancellationToken=default);
    Task SaveAsync(User user,CancellationToken cancellationToken=default);
}
