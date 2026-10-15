using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class AlarmService(IAlarmPresenter presenter,IDeviceIdentityService identity,
    IEmployeeRepository employees,IEmployeeLevelPolicyService policy,NotificationPreferences preferences,ITaskCompletionRepository completions):IAlarmService
{
    public async Task EnqueueAsync(AlarmTask task,CancellationToken cancellationToken=default)
    {
        if(await identity.GetLocalAsync(cancellationToken) is {} device)
        {
            var effective=policy.GetPolicy(task,device,await employees.GetAllAsync(cancellationToken));
            task=task with{Level=effective.Level,RequireAcknowledgement=effective.RequireAcknowledgement};
        }
        while(await preferences.IsQuietAsync(task.Level,DateTime.Now,cancellationToken))
            await Task.Delay(TimeSpan.FromSeconds(1),cancellationToken);
        if(await completions.IsCompletedAsync(task,task.ScheduledAt,cancellationToken))return;
        await presenter.ShowAsync(OccurrenceIdentity.For(task.Id,task.ScheduledAt),task,task.ScheduledAt,false,cancellationToken);
    }
}
public static class OccurrenceIdentity
{
    public static string For(string taskId,DateTime scheduledAt)=>taskId+"@"+scheduledAt.ToString("yyyyMMddHHmmssfffffff",System.Globalization.CultureInfo.InvariantCulture);
}
