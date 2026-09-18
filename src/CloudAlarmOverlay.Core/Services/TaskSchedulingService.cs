using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Recurrence;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class TaskSchedulingService(IHolidayRepository holidays,ILunarCalendarRepository lunar) : ITaskSchedulingService
{
    public async Task<DateTime?> GetNextOccurrenceAsync(AlarmTask task,DateTime after,CancellationToken cancellationToken=default)
    {
        if(!task.Enabled) return null;
        var holidayList=await holidays.GetAllAsync(cancellationToken);
        var lunarDays=task.Recurrence.StartsWith("LunarDay:",StringComparison.Ordinal)
            ? (await lunar.GetAllAsync(cancellationToken)).ToDictionary(e=>e.Date,e=>e.LunarDay) : null;
        return RecurrenceCalendar.Next(task.Recurrence,task.ScheduledAt,after,false,lunarDays,holidayList,task.SkipOnHoliday,cancellationToken);
    }
}
