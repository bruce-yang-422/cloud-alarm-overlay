using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Recurrence;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class TaskSchedulingService(IHolidayRepository holidays,ITaskCompletionRepository completions) : ITaskSchedulingService
{
    public async Task<DateTime?> GetNextOccurrenceAsync(AlarmTask task,DateTime after,CancellationToken cancellationToken=default)
    {
        if(!task.Enabled) return null;
        var holidayList=await holidays.GetAllAsync(cancellationToken);
        var next=RecurrenceCalendar.Next(task.Recurrence,task.ScheduledAt,after,false,null,holidayList,task.SkipOnHoliday,cancellationToken);
        while(next is {} at&&await completions.IsCompletedAsync(task,at,cancellationToken))
            next=RecurrenceCalendar.Next(task.Recurrence,task.ScheduledAt,at,false,null,holidayList,task.SkipOnHoliday,cancellationToken);
        return next;
    }
}
