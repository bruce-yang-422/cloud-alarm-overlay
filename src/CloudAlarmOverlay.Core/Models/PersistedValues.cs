namespace CloudAlarmOverlay.Core.Models;

// Persist these exact strings; older CSV and backups are normalized at their import boundaries.
public static class TaskSources
{
    public const string Local = "本機";
    public const string SheetA = "SheetA";
    public const string SheetB = "SheetB";
}

public static class AlarmLevels
{
    public const string Low = "一般提醒";
    public const string Mid = "重要提醒";
    public const string High = "緊急提醒";
    public const string Max = "強制通知";

    public static string Normalize(string value) => value switch
    {
        "低級" => Low,
        "中級" => Mid,
        "高級" => High,
        "最高級" => Max,
        _ => value
    };
}

public static class AcknowledgementResults
{
    public const string Acknowledged = "Acknowledged";
    public const string OverdueAcknowledged = "Overdue_Acknowledged";
    public const string OverdueUnacked = "Overdue_Unacked";
    public const string NotLaunched = "NotLaunched";
}
