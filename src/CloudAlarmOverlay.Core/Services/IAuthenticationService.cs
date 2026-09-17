namespace CloudAlarmOverlay.Core.Services;
public interface IAuthenticationService
{
    Task EnsureDefaultAdministratorAsync(CancellationToken ct=default);
    Task<bool> HasAdministratorAsync(CancellationToken ct=default);
    Task CreateInitialAsync(string username,string password,CancellationToken ct=default);
    Task<bool> AuthenticateAsync(string username,string password,CancellationToken cancellationToken=default);
    Task ChangePasswordAsync(string username,string currentPassword,string newPassword,CancellationToken cancellationToken=default);
    Task ChangeCredentialsAsync(string currentPassword,string newUsername,string newPassword,CancellationToken cancellationToken=default);
}
