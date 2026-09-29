using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class DeviceIdentityService(IDeviceRepository devices,ISettingsRepository settings,AdminSession session) : IDeviceIdentityService
{
    public Task<Device?> GetLocalAsync(CancellationToken cancellationToken=default) => devices.GetLocalAsync(cancellationToken);
    public Task SetInitialIdentityAsync(string deviceId,string displayName,CancellationToken cancellationToken=default)
        =>SetInitialIdentityAsync(deviceId,displayName,UsageModes.Company,cancellationToken);
    public async Task<string> GetUsageModeAsync(CancellationToken cancellationToken=default)
        =>(await settings.GetAsync(UsageModes.Key,cancellationToken))?.Value==UsageModes.Personal?UsageModes.Personal:UsageModes.Company;
    public async Task InitializeAccessAsync(CancellationToken cancellationToken=default)
        =>session.SetUsageMode(await devices.GetLocalAsync(cancellationToken) is not null && await GetUsageModeAsync(cancellationToken)==UsageModes.Personal);
    public async Task SetInitialIdentityAsync(string deviceId,string displayName,string usageMode,CancellationToken cancellationToken=default)
    {
        UsageModes.Validate(usageMode);
        if(string.IsNullOrWhiteSpace(deviceId)||string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("請輸入裝置代碼與顯示名稱。");
        if(await devices.GetLocalAsync(cancellationToken) is not null) throw new InvalidOperationException("身分已設定，請聯繫 IT。");
        await devices.SaveInitialAsync(new Device {DeviceId=deviceId.Trim(),DisplayName=displayName.Trim()},usageMode,cancellationToken);
        session.SetUsageMode(usageMode==UsageModes.Personal);
    }
}
