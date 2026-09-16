using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class EmployeeLevelPolicyService:IEmployeeLevelPolicyService
{
    private static readonly string[] Levels=[AlarmLevels.Low,AlarmLevels.Mid,AlarmLevels.High,AlarmLevels.Max];
    public NotificationPolicy GetPolicy(AlarmTask task,Device device,IReadOnlyList<Employee> employees)
    {
        var employee=string.IsNullOrWhiteSpace(device.DisplayName)?null:employees.FirstOrDefault(e=>
            !string.IsNullOrWhiteSpace(e.Name)&&string.Equals(e.Name,device.DisplayName,StringComparison.OrdinalIgnoreCase));
        employee??=employees.FirstOrDefault(e=>string.Equals(e.DeviceId,device.DeviceId,StringComparison.OrdinalIgnoreCase));
        var ceiling=Array.IndexOf(Levels,employee?.MaxAllowedLevel);
        var level=Array.IndexOf(Levels,task.Level);
        var effective=ceiling>=0&&ceiling<level?Levels[ceiling]:task.Level;
        var ack=employee?.RequireAckOverride?.ToUpperInvariant() switch
        {"TRUE" or "是"=>true,"FALSE" or "否"=>false,_=>task.RequireAcknowledgement};
        return new NotificationPolicy(effective,ack);
    }
}
