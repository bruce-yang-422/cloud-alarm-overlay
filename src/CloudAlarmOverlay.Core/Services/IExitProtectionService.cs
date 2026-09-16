namespace CloudAlarmOverlay.Core.Services;

public interface IExitProtectionService
{
    Task<bool> IsRequiredAsync(CancellationToken ct=default);
    Task SetRequiredAsync(bool required,CancellationToken ct=default);
    Task<string> GetModeAsync(CancellationToken ct=default);
    Task SetDedicatedPasswordAsync(string password,CancellationToken ct=default);
    Task UseAdministratorPasswordAsync(CancellationToken ct=default);
    Task<bool> VerifyDedicatedPasswordAsync(string password,CancellationToken ct=default);
}
