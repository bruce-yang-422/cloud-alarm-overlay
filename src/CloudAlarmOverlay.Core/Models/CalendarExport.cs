namespace CloudAlarmOverlay.Core.Models;

public sealed record CalendarExportOptions(DateOnly From,DateOnly To)
{
    public bool IncludeTasks {get;init;}=true;
    public bool IncludeCountdowns {get;init;}=true;
    public bool IncludeDisabledTasks {get;init;}
    public bool IncludeCompleted {get;init;}
    public bool IncludeReminders {get;init;}=true;
    public string TimeZoneId {get;init;}=TimeZoneInfo.Local.Id;
    public void Validate()
    {
        if(From.Year<1900||To.Year>9998)throw new ArgumentException("請選擇 1900–9998 年內有效的起訖日期。");
        if(To<From)throw new ArgumentException($"迄日（{To:yyyy/MM/dd}）不能早於起日（{From:yyyy/MM/dd}），請調整日期。");
        if(From.Year<=9898&&To>=From.AddYears(100))throw new ArgumentException("單次最多匯出 100 年，請縮小日期範圍或分段匯出。");
        if(!IncludeTasks&&!IncludeCountdowns)throw new ArgumentException("請至少選擇本機任務或倒數／正數事件。");
    }
}

public sealed record CalendarExportResult(string Content,int TaskEvents,int CountdownEvents,IReadOnlyList<string> Warnings)
{
    public int EventCount=>TaskEvents+CountdownEvents;
}
