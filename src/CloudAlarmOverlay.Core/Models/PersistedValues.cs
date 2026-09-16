namespace CloudAlarmOverlay.Core.Models;

// Persist these exact strings; CSV aliases are converted at the future import boundary.
public static class TaskSources
{
    public const string Local = "本機";
    public const string SheetA = "SheetA";
    public const string SheetB = "SheetB";
}

public static class AlarmLevels
{
    public const string Low = "低級";
    public const string Mid = "中級";
    public const string High = "高級";
    public const string Max = "最高級";
}

public static class AcknowledgementResults
{
    public const string Acknowledged = "Acknowledged";
    public const string OverdueAcknowledged = "Overdue_Acknowledged";
    public const string OverdueUnacked = "Overdue_Unacked";
    public const string NotLaunched = "NotLaunched";
}

