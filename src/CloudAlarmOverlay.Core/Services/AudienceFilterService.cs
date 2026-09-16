using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class AudienceFilterService : IAudienceFilterService
{
    public bool IsIncluded(AlarmTask task,Device device,IReadOnlyList<Employee> employees)
    {
        bool Matches(string? list) => (list??"").Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Any(key=>
            key==device.DeviceId || (!string.IsNullOrWhiteSpace(device.DisplayName)&&key==device.DisplayName) ||
            employees.Any(e=>e.Department==key&&(e.DeviceId==device.DeviceId ||
                (!string.IsNullOrWhiteSpace(device.DisplayName)&&e.Name==device.DisplayName))));
        return (string.IsNullOrWhiteSpace(task.TargetDeviceOrName)||Matches(task.TargetDeviceOrName))&&!Matches(task.ExcludeDeviceOrName);
    }
}
