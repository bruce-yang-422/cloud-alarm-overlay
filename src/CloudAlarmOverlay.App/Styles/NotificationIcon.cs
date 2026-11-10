using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.App.Styles;

public sealed record NotificationIcon(string Label,string PathData)
{
    public static NotificationIcon For(AlarmTask task,string? caption=null)
    {
        if(caption=="健康工具")
            return new("健康提醒","M 12,21 C 9,18 2,13 2,8 C 2,2 9,1 12,6 C 15,1 22,2 22,8 C 22,13 15,18 12,21 Z M 5,12 L 9,12 11,8 14,16 16,12 20,12");
        if(caption=="番茄鐘")
            return new("番茄鐘提醒","M 9,2 L 15,2 M 12,2 L 12,5 M 19,5 L 21,7 M 12,5 A 8,8 0 1 1 11.99,5 M 12,8 L 12,13 15,15");
        if(task.Level==AlarmLevels.Max)
            return new("強制通知","M 12,2 L 21,6 20,14 Q 18,20 12,23 Q 6,20 4,14 L 3,6 Z M 12,7 L 12,13 M 12,16 L 12,17");
        if(task.Level==AlarmLevels.High)
            return new("緊急提醒","M 10,3 Q 12,0 14,3 L 23,20 Q 24,22 21,22 L 3,22 Q 0,22 1,20 Z M 12,8 L 12,14 M 12,17 L 12,18");
        if(task.IsGoogleCalendar || task.CalendarStartAt.HasValue)
            return new("行事曆提醒","M 4,5 L 20,5 Q 22,5 22,7 L 22,21 2,21 2,7 Q 2,5 4,5 Z M 7,2 L 7,8 M 17,2 L 17,8 M 2,11 L 22,11 M 7,15 L 10,15 M 14,15 L 17,15 M 7,18 L 10,18");
        if(TaskSources.IsCloud(task.Source))
            return new("雲端任務提醒","M 6,18 C 0,18 0,10 6,10 C 6,1 19,1 19,10 C 25,10 25,18 19,18 M 12,11 L 12,22 M 8,18 L 12,22 16,18");
        if(task.Recurrence is not ("None" or ""))
            return new("重複任務提醒","M 21,10 A 9,9 0 0 0 5,6 M 3,2 L 3,7 8,7 M 3,14 A 9,9 0 0 0 19,18 M 21,22 L 21,17 16,17 M 12,7 L 12,12 15,14");
        return new("任務提醒","M 8,4 L 5,4 Q 3,4 3,6 L 3,21 Q 3,23 5,23 L 19,23 Q 21,23 21,21 L 21,6 Q 21,4 19,4 L 16,4 M 8,2 L 16,2 16,7 8,7 Z M 7,12 L 17,12 M 7,16 L 17,16 M 7,20 L 13,20");
    }
}
