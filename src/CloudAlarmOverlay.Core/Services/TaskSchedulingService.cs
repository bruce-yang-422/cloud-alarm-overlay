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
        var start=after.Date>task.ScheduledAt.Date?after.Date:task.ScheduledAt.Date;
        var end=task.Recurrence=="None"?task.ScheduledAt.Date:start.AddYears(2);
        for(var day=start;day<=end;day=day.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate=day+task.ScheduledAt.TimeOfDay;
            if(candidate<=after) continue;
            var date=DateOnly.FromDateTime(day);
            int? lunarDay=lunarDays?.GetValueOrDefault(date);
            bool match=RecurrenceRule.Matches(task.Recurrence,date,task.ScheduledAt,lunarDay);
            var today=holidayList.Where(h=>h.Date==date).ToArray();
            if(task.Recurrence.StartsWith("Weekly:",StringComparison.Ordinal) && today.Any(h=>h.Type=="補班日")) match=true;
            if(task.SkipOnHoliday && today.Any(h=>h.Type!="補班日")) match=false;
            if(match) return candidate;
        }
        return null;
    }
}
