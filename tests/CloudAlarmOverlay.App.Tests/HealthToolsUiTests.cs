using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class HealthToolsUiTests
{
    [Fact] public async Task Dashboard_navigation_settings_real_statistics_and_responsive_themes_render()
    {
        await MilestoneOneTests.RunSta(async () =>
        {
            using var fixture = new Fixture(); await fixture.Initialize();
            var health = fixture.Get<HealthToolsService>(); var timer = fixture.Get<IPomodoroService>();
            var main = fixture.Get<MainViewModel>(); var vm = main.HealthTools;
            var window = fixture.Get<MainWindow>();
            try
            {
                window.ShowInTaskbar = false; window.ShowActivated = false; window.Width = 1344; window.Height = 960;
                window.Show(); main.PageIndex = 7; await vm.LoadAsync(); window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal("健康工具", main.NavigationItems.Single(i => i.IsSelected).Title);
                Assert.Equal(4, vm.Cards.Length); Assert.Equal(0, vm.EnabledCount); Assert.False(vm.HasHistory);
                var healthView = Find<HealthToolsView>(window)!;
                var tabs = (TabControl)healthView.FindName("HealthTabs");
                Assert.Equal(new[] { "首頁", "喝水", "久坐", "螢幕休息", "伸展" }, tabs.Items.Cast<TabItem>().Select(t => (string)t.Header));
                Assert.Equal(0, tabs.SelectedIndex);
                var segments = (CloudAlarmOverlay.App.Controls.SlidingSegmentedControl)tabs.Template.FindName("PART_HealthSegments", tabs);
                Assert.Equal(vm.PageNames, segments.Items.Cast<string>());
                segments.SelectedIndex = 1; Assert.Equal(1, vm.SelectedTabIndex); Assert.Equal(1, tabs.SelectedIndex);
                var indicator = (Border)segments.Template.FindName("PART_Indicator", segments);
                await Task.Delay(200);
                Assert.True(Canvas.GetLeft(indicator) > 0); Assert.True(indicator.ActualWidth > 0);
                vm.Cards[2].EditCommand.Execute(null); Assert.Equal(3, vm.SelectedTabIndex); Assert.False(vm.SettingsOpen);
                var screen = vm.Cards[2];
                screen.Editor!.Interval = "bad"; await screen.SaveCommand.ExecuteAsync(null); Assert.NotEmpty(screen.Error);
                screen.Editor.Interval = "20"; screen.Editor.Start = "10:00"; screen.Editor.Enabled = true;
                await screen.SaveCommand.ExecuteAsync(null); vm.Refresh(); Assert.Empty(screen.Error); Assert.Equal(1, vm.EnabledCount);
                vm.Cards[0].Editor!.Interval = "77";
                vm.SelectedTabIndex = 1; vm.SelectedTabIndex = 3; vm.SelectedTabIndex = 0;
                await vm.LoadAsync();
                Assert.Equal("77", vm.Cards[0].Editor!.Interval); Assert.Equal("20", screen.Editor.Interval);
                vm.Cards[0].ResetDraftCommand.Execute(null); Assert.Equal("60", vm.Cards[0].Editor!.Interval);
                await health.SaveToolAsync(new() { Kind = "Water", Enabled = true, IntervalMinutes = 20, Start = new(10, 0), End = new(18, 0), Days = 127 });
                await timer.SaveOptionsAsync(new() { FocusMinutes = 90, SoundEnabled = false }); await timer.StartAsync();
                await fixture.AdvanceTo(10, 25); await timer.PauseAsync();
                var pending = health.Ready(timer.State, false, false); Assert.Equal(2, pending.Length);
                await health.MarkDisplayedAsync(pending.Select(p => p.Id), "first"); await health.AcknowledgeAsync("first");
                await timer.StartAsync(); await fixture.AdvanceTo(10, 40); vm.Refresh();
                Assert.Equal(1, vm.TodayNotifications); Assert.Equal(2, vm.TodayItems); Assert.Equal(2, vm.PendingCount);
                Assert.Equal(2, vm.Week.Sum(d => d.Total)); Assert.True(vm.HasHistory);
                window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Screenshot(window, "health-tools-light");
                var view = Find<HealthToolsView>(window)!;
                var scroll = (ScrollViewer)view.FindName("PageScroll");
                scroll.ScrollToBottom(); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Screenshot(window, "health-tools-trends");
                scroll.ScrollToTop();
                AdaptiveBrushExtension.Apply(true); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Screenshot(window, "health-tools-dark");
                for (var i = 0; i < vm.Cards.Length; i++)
                {
                    vm.Cards[i].EditCommand.Execute(null); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Same(vm.Cards[i], Find<HealthToolView>(window)!.DataContext);
                    var mode=Find<ComboBox>(Find<HealthToolView>(window)!)!;
                    Assert.Equal(new[]{"每天","工作日","自訂星期"},mode.Items.Cast<string>());
                    mode.SelectedItem="工作日";Assert.False(vm.Cards[i].Editor!.IsCustomDays);
                    await vm.Cards[i].SaveCommand.ExecuteAsync(null);Assert.Empty(vm.Cards[i].Error);
                    Assert.Equal("Workdays",health.Snapshot.Tools[i].Options.DayMode);Assert.Contains("工作日",vm.Cards[i].Window);
                    vm.Cards[i].ResetDraftCommand.Execute(null);Assert.Equal("工作日",vm.Cards[i].Editor!.DayMode);
                    Assert.All(vm.Cards[i].History, row => Assert.Equal(vm.Cards[i].Name, row.Name));
                    await Task.Delay(180); Screenshot(window, "health-tools-tab-" + i);
                }
                vm.SelectedTabIndex = 3; window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Screenshot(window, "health-tools-settings");
                AdaptiveBrushExtension.Apply(false);
                window.Width = 1050; window.Height = 680; window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Screenshot(window, "health-tools-small");
                Assert.Equal(1, new HealthColumnsConverter().Convert(600d, typeof(int), "", System.Globalization.CultureInfo.InvariantCulture));
                await vm.TogglePauseCommand.ExecuteAsync(null); Assert.Equal("立即恢復", vm.PauseLabel); Assert.Equal(0, vm.PendingCount);
                await vm.TogglePauseCommand.ExecuteAsync(null); Assert.False(health.IsPaused);
                vm.OpenSettingsCommand.Execute(null); window.UpdateLayout();
                var alignment = (CloudAlarmOverlay.App.Controls.SlidingSegmentedControl)view.FindName("PomodoroCoordinationMode");
                Assert.Equal(new[] { "自動對齊", "覆蓋", "延後" }, alignment.Items.Cast<string>());
                Assert.Equal("延後", alignment.SelectedItem);
                Assert.Equal("右邊", vm.NotificationPosition);
                vm.NotificationPosition = "左邊"; alignment.SelectedItem = "覆蓋"; await vm.SaveSettingsCommand.ExecuteAsync(null);
                Assert.Equal("Override", health.Snapshot.EffectiveMode);
                Assert.Equal("Left", health.Snapshot.NotificationSide);
                vm.OpenSettingsCommand.Execute(null); window.UpdateLayout();
                Assert.Equal("覆蓋", alignment.SelectedItem);
                Assert.Equal("左邊", vm.NotificationPosition);
                vm.NotificationPosition = "右邊"; alignment.SelectedItem = "自動對齊"; vm.CloseSettingsCommand.Execute(null);
                Assert.Equal("Override", health.Snapshot.EffectiveMode);
                Assert.Equal("Left", health.Snapshot.NotificationSide);
                vm.OpenSettingsCommand.Execute(null); Assert.Equal("覆蓋", vm.CoordinationMode);
                alignment.SelectedItem = "自動對齊"; vm.NotificationPosition = "置中";
                await vm.SaveSettingsCommand.ExecuteAsync(null); Assert.Equal("AutoAlign", health.Snapshot.EffectiveMode);
                Assert.Equal("Center", health.Snapshot.NotificationSide);
                vm.OpenSettingsCommand.Execute(null); window.UpdateLayout();
                Assert.True(vm.SettingsOpen);
                Screenshot(window, "health-tools-coordination");
                vm.CloseSettingsCommand.Execute(null);
                main.PageIndex = 0; main.PageIndex = 7; Assert.Equal(0, vm.SelectedTabIndex);
            }
            finally { AdaptiveBrushExtension.Apply(false); window.Close(); }
        });
    }
    [Fact] public async Task Side_panel_uses_thirty_percent_topmost_both_sides_and_stays_until_confirmation()
    {
        await MilestoneOneTests.RunSta(async () =>
        {
            using var fixture = new Fixture(); await fixture.Initialize();
            var health = fixture.Get<HealthToolsService>();
            await health.SaveToolAsync(new() { Kind = "Screen", Enabled = true, IntervalMinutes = 20, Start = new(10, 0), End = new(18, 0), Days = 127 });
            await fixture.AdvanceTo(10, 50);
            var coordinator = fixture.Get<HealthNotificationCoordinator>();
            try
            {
                await coordinator.UpdateAsync(new("Focus", "Running"), null, false, false, "暗色", "依提醒等級", default);
                var window = PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>().Select(s => s.RootVisual)
                    .OfType<AlarmWindow>().Single(w => w.DataContext is AlarmViewModel { Caption: "健康工具" });
                Assert.False(window.ShowActivated); Assert.True(window.Topmost);
                string? confirmTrace = null;
                ((AlarmViewModel)window.DataContext).Confirmed += () => confirmTrace = Environment.StackTrace;
                // The test shares the interactive desktop. Physical input must not
                // confirm the panel while we verify that no automatic close occurs.
                window.PreviewMouseDown += (_, e) => e.Handled = true;
                window.PreviewKeyDown += (_, e) => e.Handled = true;
                Assert.Equal("確定", ((AlarmViewModel)window.DataContext).ConfirmLabel);
                var work = SystemParameters.WorkArea;
                Assert.Equal(work.Width * .3, window.Width, 2); Assert.Equal(work.Height, window.Height, 2);
                Assert.Equal(work.Right - window.Width, window.Left, 2); Assert.Equal(work.Top, window.Top, 2);
                window.UpdateLayout(); Screenshot(window, "health-notification-right");
                Assert.Single(health.Snapshot.Events, e => e.TriggeredAt is not null);
                await health.SavePreferencesAsync(true, "Left");
                await coordinator.UpdateAsync(new("Focus", "Running"), null, false, false, "暗色", "依提醒等級", default);
                Assert.Equal(work.Left, window.Left, 2); Assert.Equal(work.Top, window.Top, 2);
                window.UpdateLayout(); Screenshot(window, "health-notification-left");
                await health.SavePreferencesAsync("Delay", "Center");
                await coordinator.UpdateAsync(new("Focus", "Running"), null, false, false, "暗色", "依提醒等級", default);
                Assert.Equal(work.Left + (work.Width - window.Width) / 2, window.Left, 2);
                Assert.Equal(work.Top, window.Top, 2);
                window.UpdateLayout(); Screenshot(window, "health-notification-center");
                // Ordinary toast reflow must not move a docked health panel.
                var ordinary = new AlarmWindow(new(new() { Id = "other", Title = "一般提醒", ScheduledAt = fixture.Clock.At, CreatedAt = fixture.Clock.At, UpdatedAt = fixture.Clock.At }, "", false), AlarmLevels.Mid);
                try { ordinary.ShowActivated = false; ordinary.Show(); Assert.Equal(work.Left + (work.Width - window.Width) / 2, window.Left, 2); }
                finally { ordinary.Finish(false); }
                window.Close(); Assert.True(window.IsVisible); // A native close is not confirmation.
                await Task.Delay(11000); Assert.True(window.IsVisible, $"Visibility={window.Visibility}; state={window.WindowState}; completion={window.Completion.Status}; result={(window.Completion.IsCompletedSuccessfully ? window.Completion.Result : null)}; confirmed={confirmTrace}"); Assert.False(window.Completion.IsCompleted);
                await coordinator.UpdateAsync(new(), null, true, false, "暗色", "依提醒等級", default);
                Assert.True(window.IsVisible); // Quiet hours only suppress new deliveries.
                Assert.All(health.Snapshot.Events, e => Assert.Null(e.AcknowledgedAt));
                ((AlarmViewModel)window.DataContext).ConfirmCommand.Execute(null);
                for (var i = 0; i < 30 && health.Snapshot.Events.Any(e => e.TriggeredAt is not null && e.AcknowledgedAt is null); i++) await Task.Delay(10);
                Assert.False(window.IsVisible);
                Assert.NotNull(Assert.Single(health.Snapshot.Events, e => e.TriggeredAt is not null).AcknowledgedAt);
                await coordinator.UpdateAsync(new("Focus", "Running"), null, false, false, "暗色", "依提醒等級", default);
                Assert.DoesNotContain(PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>().Select(s => s.RootVisual)
                    .OfType<AlarmWindow>(), w => w.DataContext is AlarmViewModel { Caption: "健康工具" });
            }
            finally { coordinator.Close(); }
        });
    }
    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find<T>(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    [Fact] public void Day_mode_switch_preserves_custom_selection_and_builds_everyday_independently()
    {
        var editor=new HealthToolEditor(new(){Days=10});Assert.Equal("自訂星期",editor.DayMode);Assert.True(editor.IsCustomDays);
        editor.DayMode="工作日";Assert.Equal("Workdays",editor.Build().DayMode);
        editor.DayMode="每天";Assert.Equal("Everyday",editor.Build().DayMode);
        editor.DayMode="自訂星期";Assert.Equal(10,editor.Build().Days);
        foreach(var day in editor.Days)day.Selected=false;
        Assert.Throws<ArgumentException>(()=>editor.Build());editor.DayMode="工作日";Assert.Equal("Workdays",editor.Build().DayMode);
    }
    [Fact] public async Task Pending_health_items_merge_into_existing_pomodoro_notice_and_acknowledge_only_on_click()
    {
        await MilestoneOneTests.RunSta(async () =>
        {
            using var fixture = new Fixture(); await fixture.Initialize();
            var health = fixture.Get<HealthToolsService>();
            foreach (var kind in new[] { "Water", "Screen" }) await health.SaveToolAsync(new() { Kind = kind, Enabled = true, IntervalMinutes = kind == "Water" ? 60 : 20, Start = new(10, 0), End = new(18, 0), Days = 127 });
            await health.SavePreferencesAsync("AutoAlign", "Right");
            await health.ObservePomodoroAsync(new("Focus", "Running"));
            await fixture.AdvanceTo(10, 20);
            var now = fixture.Clock.At;
            var vm = new AlarmViewModel(new() { Id = "pomodoro", Title = "專注時間結束，休息一下吧", Description = "按下確認後開始休息。", ScheduledAt = now, CreatedAt = now, UpdatedAt = now }, "", false, "番茄鐘");
            var window = new AlarmWindow(vm, AlarmLevels.Mid);
            var coordinator = fixture.Get<HealthNotificationCoordinator>();
            try
            {
                window.ShowActivated = false; window.Show();
                await coordinator.UpdateAsync(new("Focus", "AwaitingConfirmation"), window, false, false, "亮色", "依提醒等級", default);
                Assert.Contains("喝點水", vm.Description); Assert.Contains("讓眼睛離開螢幕", vm.Description);
                Assert.Equal("開始休息", vm.ConfirmLabel);
                Assert.Equal(SystemParameters.WorkArea.Width * .3, window.Width, 2);
                Assert.Equal(SystemParameters.WorkArea.Height, window.Height, 2);
                Assert.True(window.Topmost);
                Assert.Equal(2, health.Snapshot.Events.Length); Assert.All(health.Snapshot.Events, e => Assert.Null(e.AcknowledgedAt));
                await coordinator.UpdateAsync(new("Focus", "AwaitingConfirmation"), window, false, false, "亮色", "依提醒等級", default);
                Assert.Equal(2, health.Snapshot.Events.Length);
                window.UpdateLayout(); Screenshot(window, "health-pomodoro-merged");
                vm.ConfirmCommand.Execute(null);
                for (var i = 0; i < 30 && health.Snapshot.Events.Any(e => e.AcknowledgedAt is null); i++) await Task.Delay(10);
                Assert.All(health.Snapshot.Events, e => Assert.NotNull(e.AcknowledgedAt));
            }
            finally { coordinator.Close(); window.Finish(false); }
        });
    }
    private static void Screenshot(Window window, string name)
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots")); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTime At = new(2026, 9, 29, 10, 0, 0);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(At, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => At.Ticks;
    }
    private sealed class Fixture : IDisposable, IAppPaths
    {
        private readonly IHost host;
        public Clock Clock { get; } = new();
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "CloudAlarmHealthUi", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public Fixture() => host = new HostBuilder().ConfigureServices(s => { CompositionRoot.ConfigureServices(s); s.AddSingleton<IAppPaths>(this); s.AddSingleton<TimeProvider>(Clock); }).Build();
        public T Get<T>() where T : notnull => host.Services.GetRequiredService<T>();
        public async Task Initialize() { await Get<IDatabaseInitializer>().InitializeAsync(); await Get<IPomodoroService>().InitializeAsync(); await Get<HealthToolsService>().InitializeAsync(); }
        public async Task AdvanceTo(int hour, int minute)
        {
            var target = Clock.At.Date.AddHours(hour).AddMinutes(minute);
            while (Clock.At < target) { Clock.At = Clock.At.AddSeconds(10); await Get<HealthToolsService>().TickAsync(); await Get<IPomodoroService>().TickAsync(); }
        }
        public void Dispose() { host.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
