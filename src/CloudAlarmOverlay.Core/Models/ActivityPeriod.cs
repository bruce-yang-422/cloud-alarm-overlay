namespace CloudAlarmOverlay.Core.Models;

public static class ActivityPeriod
{
    public static void Validate(AlarmTask task)
    {
        if(task.ActivityStartAt is null && task.ActivityEndAt is null && !task.ActivityAllDay)return;
        if(task.ActivityStartAt is not {} start || task.ActivityEndAt is not {} end || end<=start)
            throw new ArgumentException("活動結束必須晚於開始；請完整填寫活動期間。");
        if(start.Year<1900 || end.Year>9998)throw new ArgumentException("活動日期限 1900–9998 年。");
        if(task.ActivityAllDay && (start.TimeOfDay!=TimeSpan.Zero || end.TimeOfDay!=TimeSpan.Zero))
            throw new ArgumentException("全天活動須使用完整日期。");
        if(task.Recurrence!="None")throw new ArgumentException("跨日／區間活動目前不支援重複規則，請將提醒設為不重複。");
    }
}
