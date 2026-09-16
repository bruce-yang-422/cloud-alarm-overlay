using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class DeviceIdentityService(IDeviceRepository devices) : IDeviceIdentityService
{
    public Task<Device?> GetLocalAsync(CancellationToken cancellationToken=default) => devices.GetLocalAsync(cancellationToken);
    public async Task SetInitialIdentityAsync(string deviceId,string displayName,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(deviceId)||string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("請輸入 IT 提供的裝置代碼與名稱。");
        if(await devices.GetLocalAsync(cancellationToken) is not null) throw new InvalidOperationException("身分已設定，請聯繫 IT。");
        await devices.SaveLocalAsync(new Device {DeviceId=deviceId.Trim(),DisplayName=displayName.Trim()},cancellationToken);
    }
}
