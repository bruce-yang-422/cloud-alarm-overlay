namespace CloudAlarmOverlay.Core.Services;

public interface IPrivateSourceStore
{
    Task ClearAsync(string source,CancellationToken ct=default,bool preserveCalendarReminders=false);
}
