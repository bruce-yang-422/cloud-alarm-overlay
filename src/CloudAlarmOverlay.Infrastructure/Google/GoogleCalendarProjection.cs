using System.Globalization;
using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Infrastructure.Google;

internal static class GoogleCalendarProjection
{
    internal static AlarmTask? Project(JsonElement item,GoogleSource source,string label,string calendarZone,DateTimeOffset now,JsonElement defaultReminders=default)
    {
        if(GoogleApi.Text(item,"status")=="cancelled")return null;
        if(item.TryGetProperty("attendees",out var attendees)&&attendees.EnumerateArray().Any(a=>a.TryGetProperty("self",out var self)&&self.ValueKind==JsonValueKind.True&&GoogleApi.Text(a,"responseStatus")=="declined"))return null;
        if(!item.TryGetProperty("start",out var start))throw new FormatException("行程缺少開始時間。");
        DateTimeOffset at;
        if(GoogleApi.Text(start,"dateTime") is {Length:>0} dateTime)
            at=DateTimeOffset.Parse(dateTime,CultureInfo.InvariantCulture,DateTimeStyles.None);
        else
        {
            if(!source.IncludeAllDay)return null;
            var date=DateTime.ParseExact(GoogleApi.Text(start,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture);
            var zone=TimeZoneInfo.FindSystemTimeZoneById(calendarZone);
            at=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date,DateTimeKind.Unspecified),zone));
        }
        DateTime? activityStart=null,activityEnd=null;
        var allDay=GoogleApi.Text(start,"dateTime").Length==0;
        if(item.TryGetProperty("end",out var end))
        {
            activityStart=allDay?DateTime.ParseExact(GoogleApi.Text(start,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture):at.LocalDateTime;
            activityEnd=allDay?DateTime.ParseExact(GoogleApi.Text(end,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture)
                :DateTimeOffset.Parse(GoogleApi.Text(end,"dateTime"),CultureInfo.InvariantCulture,DateTimeStyles.None).LocalDateTime;
            if(activityEnd<=activityStart)throw new FormatException("行程結束時間必須晚於開始時間。");
        }
        // Calendar popup reminders map to this app's single desktop reminder. Email-only reminders do not.
        var reminderList=defaultReminders;
        if(item.TryGetProperty("reminders",out var reminders))
        {
            var useDefault=reminders.TryGetProperty("useDefault",out var use)&&use.ValueKind==JsonValueKind.True;
            if(!useDefault)reminderList=reminders.TryGetProperty("overrides",out var overrides)?overrides:default;
        }
        var minutes=reminderList.ValueKind==JsonValueKind.Array
            ?reminderList.EnumerateArray().Where(r=>GoogleApi.Text(r,"method")=="popup")
                .Select(r=>r.TryGetProperty("minutes",out var m)&&m.TryGetInt32(out var value)&&value>=0&&value<=40320?(int?)value:null)
                .Where(m=>m.HasValue).Max():null;
        var reminderAt=at.AddMinutes(-(minutes??0));
        // Keep a small past window for stable cache identity; the normal scheduler never mass-replays old tasks.
        var id=GoogleApi.Text(item,"id");if(id.Length==0)throw new FormatException("行程缺少識別碼。");
        var title=GoogleApi.Text(item,"summary");if(title.Length==0)title="Google 日曆行程";
        return new(){Id=source.CacheSource+":"+id,ExternalId=id,Source=source.CacheSource,Title=title[..Math.Min(title.Length,50)],
            Description=GoogleApi.Text(item,"description"),Note=label+"\n"+GoogleApi.Text(item,"htmlLink"),ScheduledAt=reminderAt.LocalDateTime,Enabled=minutes.HasValue,
            CalendarStartAt=at.LocalDateTime,GoogleReminderAt=reminderAt.LocalDateTime,GoogleReminderEnabled=minutes.HasValue,
            ActivityStartAt=activityStart,ActivityEndAt=activityEnd,ActivityAllDay=activityStart.HasValue&&allDay,
            Level=AlarmLevels.Low,RequireAcknowledgement=false,CreatedAt=now.LocalDateTime,UpdatedAt=now.LocalDateTime};
    }
}
