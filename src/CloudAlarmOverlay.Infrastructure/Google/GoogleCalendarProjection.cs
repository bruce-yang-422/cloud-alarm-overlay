using System.Globalization;
using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Infrastructure.Google;

internal static class GoogleCalendarProjection
{
    internal static AlarmTask? Project(JsonElement item,GoogleSource source,string label,string calendarZone,DateTimeOffset now)
    {
        if(GoogleApi.Text(item,"status")=="cancelled")return null;
        if(item.TryGetProperty("attendees",out var attendees)&&attendees.EnumerateArray().Any(a=>a.TryGetProperty("self",out var self)&&self.ValueKind==JsonValueKind.True&&GoogleApi.Text(a,"responseStatus")=="declined"))return null;
        if(!item.TryGetProperty("start",out var start))throw new FormatException("行程缺少開始時間。");
        DateTimeOffset at;
        if(GoogleApi.Text(start,"dateTime") is {Length:>0} dateTime)
            at=DateTimeOffset.Parse(dateTime,CultureInfo.InvariantCulture,DateTimeStyles.None).AddMinutes(-source.ReminderMinutes);
        else
        {
            if(!source.IncludeAllDay)return null;
            var date=DateTime.ParseExact(GoogleApi.Text(start,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture).AddHours(source.AllDayHour);
            var zone=TimeZoneInfo.FindSystemTimeZoneById(calendarZone);
            at=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date,DateTimeKind.Unspecified),zone));
        }
        // Keep a small past window for stable cache identity; the normal scheduler never mass-replays old tasks.
        var id=GoogleApi.Text(item,"id");if(id.Length==0)throw new FormatException("行程缺少識別碼。");
        var title=GoogleApi.Text(item,"summary");if(title.Length==0)title="Google 日曆行程";
        return new(){Id=source.CacheSource+":"+id,ExternalId=id,Source=source.CacheSource,Title=title[..Math.Min(title.Length,50)],
            Description=GoogleApi.Text(item,"description"),Note=label+"\n"+GoogleApi.Text(item,"htmlLink"),ScheduledAt=at.LocalDateTime,
            Level=AlarmLevels.Low,RequireAcknowledgement=false,CreatedAt=now.LocalDateTime,UpdatedAt=now.LocalDateTime};
    }
}
