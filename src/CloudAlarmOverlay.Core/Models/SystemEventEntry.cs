namespace CloudAlarmOverlay.Core.Models;
public sealed record SystemEventEntry
{
    public long Id {get;init;}
    public DateTime Time {get;init;}=DateTime.Now;
    public required string EventType {get;init;}
    public required string Message {get;init;}
}
