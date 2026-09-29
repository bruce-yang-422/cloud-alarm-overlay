using System.Runtime.InteropServices;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Logging;

namespace CloudAlarmOverlay.App.Services;

// All window access is on the WPF dispatcher, shared with PomodoroWorker.
public sealed class HealthNotificationCoordinator(HealthToolsService health, ILogger<HealthNotificationCoordinator> logger)
{
    private AlarmWindow? standalone;
    private readonly Dictionary<AlarmWindow, Delivery> deliveries = [];
    private sealed class Delivery
    {
        public string BatchId { get; } = Guid.NewGuid().ToString("N");
        public Dictionary<string, HealthPending> Items { get; } = [];
        public Task Persisting { get; set; } = Task.CompletedTask;
    }
    public async Task UpdateAsync(PomodoroState pomodoro, AlarmWindow? pomodoroNotice, bool quiet, bool unavailable, string mode, string scheme, CancellationToken ct)
    {
        await health.ObservePomodoroAsync(pomodoro, ct);
        if (quiet || unavailable || health.IsPaused)
        {
            // Suppress new deliveries; an already displayed panel remains until confirmed.
            return;
        }
        var ready = health.Ready(pomodoro, quiet, unavailable);
        // An open health panel is folded into the Pomodoro confirmation when focus ends.
        if (pomodoroNotice is not null && standalone is not null)
        {
            if (deliveries.TryGetValue(standalone, out var prior))
            {
                var target = Attach(pomodoroNotice);
                foreach (var item in prior.Items) target.Items[item.Key] = item.Value;
            }
            standalone.Finish(false); standalone = null;
        }
        var notice = pomodoroNotice ?? standalone;
        if (notice is null && ready.Length == 0) return;
        if (notice is null)
        {
            var now = health.Now;
            var task = new AlarmTask { Id = "health", Title = "健康休息時間", Level = AlarmLevels.Mid, ScheduledAt = now, CreatedAt = now, UpdatedAt = now };
            notice = standalone = new AlarmWindow(new AlarmViewModel(task, "", false, "健康工具"), AlarmLevels.Mid, colorMode: mode, colorScheme: scheme);
            Attach(notice);
        }
        var delivery = Attach(notice);
        foreach (var item in ready) delivery.Items[item.Id] = item;
        var valid = delivery.Items.Values.ToArray();
        ((AlarmViewModel)notice.DataContext).HealthDescription = valid.Length == 0 ? "" : "健康提醒\n" + string.Join("\n", valid.Select(p => p.Kind).Distinct().Select(k => "• " + HealthTools.Advice(k)));
        if (notice == standalone && valid.Length == 0) { notice.Finish(false); standalone = null; return; }
        if (valid.Length > 0) notice.DockHealth(health.Snapshot.NotificationSide);
        if (!notice.IsVisible) notice.Show();
        // Commit only after Show succeeds. A failed write is retried on the same window.
        delivery.Persisting = health.MarkDisplayedAsync(delivery.Items.Keys, delivery.BatchId, ct);
        await delivery.Persisting;
    }
    private Delivery Attach(AlarmWindow window)
    {
        if (deliveries.TryGetValue(window, out var delivery)) return delivery;
        delivery = new(); deliveries.Add(window, delivery);
        var vm = (AlarmViewModel)window.DataContext;
        vm.Confirmed += () => _ = ConfirmAsync(delivery);
        window.Closed += (_, _) => { deliveries.Remove(window); if (ReferenceEquals(standalone, window)) standalone = null; };
        return delivery;
    }
    private async Task ConfirmAsync(Delivery delivery)
    {
        try
        {
            await delivery.Persisting;
            // Items migrated from another open panel retain their original batch identity.
            var ids = delivery.Items.Keys.ToHashSet();
            foreach (var batch in health.Snapshot.Events.Where(e => ids.Contains(e.Id) && e.BatchId is not null).Select(e => e.BatchId!).Distinct())
                await health.AcknowledgeAsync(batch);
        }
        catch (Exception ex) { logger.LogError(ex, "健康提醒確認儲存失敗"); }
    }
    public void Close() { standalone?.Finish(false); standalone = null; }
}

internal static class DesktopAvailability
{
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SwitchDesktop(IntPtr desktop);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(IntPtr desktop);
    public static bool IsUnavailable()
    {
        var desktop = OpenInputDesktop(0, false, 0x0100);
        if (desktop == IntPtr.Zero) return true;
        try { return !SwitchDesktop(desktop); } finally { CloseDesktop(desktop); }
    }
}
