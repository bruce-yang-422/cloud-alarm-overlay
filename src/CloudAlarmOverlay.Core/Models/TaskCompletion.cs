namespace CloudAlarmOverlay.Core.Models;

public sealed record TaskCompletion
{
    public string Source {get;init;}="";
    public string TaskId {get;init;}="";
    public DateTime OccurrenceAt {get;init;}
    public DateTime CompletedAt {get;init;}
    public string SnapshotJson {get;init;}="";
    public static DateTime Occurrence(AlarmTask task,DateTime at)=>task.IsGoogleCalendar?task.CalendarStartAt!.Value:at;
    public bool Matches(AlarmTask task,DateTime at)=>Source==task.Source&&TaskId==task.Id&&OccurrenceAt==Occurrence(task,at);
}
