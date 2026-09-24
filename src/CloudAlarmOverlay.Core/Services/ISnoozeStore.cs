namespace CloudAlarmOverlay.Core.Services;
public interface ISnoozeStore
{
    Task<int> CountAsync(string id, CancellationToken ct = default);
    Task DeferAsync(string id, DateTime until, CancellationToken ct = default);
    Task MissAsync(string id, CancellationToken ct = default);
}
public static class SnoozePolicy
{
    public const int MaximumCount = 3;
    public static bool CanDefer(string level, int count, bool allowUrgent) => count < MaximumCount &&
        (level is Models.AlarmLevels.Low or Models.AlarmLevels.Mid or Models.AlarmLevels.Max || level == Models.AlarmLevels.High && allowUrgent);
    public static bool IsValidMinutes(int minutes) => minutes is 5 or 10 or 15 or 30;
    public static bool Overlaps(DateTime until, DateTime? next) => next is {} at && until >= at;
}
