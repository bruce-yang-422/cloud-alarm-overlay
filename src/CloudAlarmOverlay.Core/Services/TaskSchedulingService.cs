using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Recurrence;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class TaskSchedulingService(IHolidayRepository holidays) : ITaskSchedulingService
{
    public async Task<DateTime?> GetNextOccurrenceAsync(AlarmTask task,DateTime after,CancellationToken cancellationToken=default)
    {
        if(!task.Enabled) return null;
        var holidayList=await holidays.GetAllAsync(cancellationToken);
        return RecurrenceCalendar.Next(task.Recurrence,task.ScheduledAt,after,false,null,holidayList,task.SkipOnHoliday,cancellationToken);
    }
}
