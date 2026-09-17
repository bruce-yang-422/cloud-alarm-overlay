using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;

public sealed class MilestoneOneTests
{
    [Fact]
    public async Task Alarm_worker_reacts_to_changes_fires_once_and_within_one_second()
    {
        var paths = new Paths(); var capture = new CapturePresenter();
        using var host = Build(paths, capture);
        try
        {
            await host.StartAsync();
            await host.Services.GetRequiredService<IDeviceIdentityService>().SetInitialIdentityAsync("TEST", "測試");
            var at = DateTime.Now.AddSeconds(2);
            var task = Alarm(at);
            await host.Services.GetRequiredService<ITaskService>().SaveLocalAsync(task);
            var triggered = await capture.Fired.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.InRange((triggered - at).TotalMilliseconds, 0, 999);
            host.Services.GetRequiredService<ChangeSignal>().Notify();
            await Task.Delay(250);
            Assert.Equal(1, capture.Count);
        }
        finally { await host.StopAsync(); Directory.Delete(paths.DataDirectory, true); }
    }
    [Fact]
    public async Task Startup_records_missed_occurrence_without_showing_overlay()
    {
        var paths = new Paths(); var capture = new CapturePresenter();
        using var host = Build(paths, capture);
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            await host.Services.GetRequiredService<IDeviceIdentityService>().SetInitialIdentityAsync("TEST", "測試");
            var at = DateTime.Now.AddMinutes(-2);
            await host.Services.GetRequiredService<ITaskRepository>().SaveLocalAsync(Alarm(at));
            await host.Services.GetRequiredService<ISettingsRepository>().SaveAsync(new Setting { Key = "AlarmCheckpoint", Value = at.AddMinutes(-1).ToString("O") });
            await host.StartAsync();
            IReadOnlyList<AcknowledgementLog> logs = [];
            for (var i = 0; i < 30 && logs.Count == 0; i++)
            { await Task.Delay(50); logs = await host.Services.GetRequiredService<IAckLogRepository>().GetRangeAsync(at.AddMinutes(-1), DateTime.Now); }
            Assert.Equal("NotLaunched", Assert.Single(logs).Result); Assert.Equal(0, capture.Count);
        }
        finally { await host.StopAsync(); Directory.Delete(paths.DataDirectory, true); }
    }
    [Fact]
    public async Task Wpf_pages_render_and_maximum_notification_requires_correct_code()
    {
        await RunSta(async () =>
        {
            var paths = new Paths();
            using var host = Build(paths, new CapturePresenter());
            MainWindow? window = null;
            AlarmWindow? alarm = null;
            try
            {
                await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
                await host.Services.GetRequiredService<IDeviceIdentityService>().SetInitialIdentityAsync("PREVIEW-001", "介面測試");
                var service = host.Services.GetRequiredService<ITaskService>();
                await service.SaveLocalAsync(Alarm(DateTime.Now.AddHours(1)) with { Title = "確認今日工作進度", Level = AlarmLevels.High });
                await service.SaveLocalAsync(Alarm(DateTime.Now.AddHours(2)) with { Id = "second", Title = "整理本週會議記錄", Recurrence = "Daily", Level = AlarmLevels.Low });
                var vm = host.Services.GetRequiredService<MainViewModel>();
                await vm.InitializeAsync();
                window = host.Services.GetRequiredService<MainWindow>(); window.ShowInTaskbar = false; window.ShowActivated = false; window.Show();
                var screenshotDirectory = Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR");
                for (var page = 0; page < 5; page++)
                {
                    vm.PageIndex = page; window.UpdateLayout();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.True(window.IsVisible); Assert.True(((FrameworkElement)window.Content).ActualWidth > 800);
                    if (screenshotDirectory is not null)
                    {
                        Directory.CreateDirectory(screenshotDirectory);
                        var content = (FrameworkElement)window.Content;
                        var dpi = VisualTreeHelper.GetDpi(content);
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
                        (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                        bitmap.Render(content);
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(screenshotDirectory, $"milestone1-page-{page}.png")); png.Save(file);
                    }
                }
                window.StartTray(host.Services.GetRequiredService<ChangeSignal>(), () => Task.CompletedTask);
                window.Close(); Assert.False(window.IsVisible);
                window.Open(); Assert.True(window.IsVisible);
                var editorVm = new TaskEditorViewModel(service, null, false) { TaskTitle = "編輯器儲存驗證", Repeat = "每月", MonthDay = 20, Date = DateTime.Today.AddDays(1), SelectedHour = 23, SelectedMinute = 7 };
                var editor = new TaskEditorWindow { DataContext = editorVm, Owner = window };
                editor.Show(); editor.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var hourWheel = (CloudAlarmOverlay.App.Controls.TimeWheel)editor.FindName("HourWheel");
                hourWheel.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                    { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
                Assert.Equal(0, editorVm.SelectedHour);
                hourWheel.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(hourWheel)!, 0, System.Windows.Input.Key.Down)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                Assert.Equal(23, editorVm.SelectedHour);
                var hourInput = (System.Windows.Controls.TextBox)hourWheel.FindName("SelectedInput");
                hourInput.Text = "08";
                hourInput.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(hourInput)!, 0, System.Windows.Input.Key.Tab)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                Assert.Equal(8, editorVm.SelectedHour);
                ((System.Windows.Controls.Button)hourWheel.FindName("IncreaseButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(9, editorVm.SelectedHour);
                ((System.Windows.Controls.Button)hourWheel.FindName("DecreaseButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(8, editorVm.SelectedHour);
                editorVm.SelectedHour = 23;
                var saveButton = (FrameworkElement)editor.FindName("SaveButton");
                var editorRoot = (FrameworkElement)editor.Content;
                var saveBounds = saveButton.TransformToAncestor(editorRoot).TransformBounds(new Rect(saveButton.RenderSize));
                Assert.True(saveBounds.Bottom <= editorRoot.ActualHeight);
                await editorVm.SaveCommand.ExecuteAsync(null);
                Assert.Equal("", editorVm.Error);
                Assert.Contains(await host.Services.GetRequiredService<ITaskRepository>().GetAllAsync(), t => t.Title == "編輯器儲存驗證" && t.Recurrence == "Monthly:20");
                editorVm.Repeat = "每週";
                editorVm.Weekdays[0].IsSelected = false;
                editorVm.Weekdays[6].IsSelected = true;
                await editorVm.SaveCommand.ExecuteAsync(null);
                var edited = (await host.Services.GetRequiredService<ITaskRepository>().GetAllAsync()).Single(t => t.Title == "編輯器儲存驗證");
                Assert.Equal("Weekly:2,3,4,5,7", edited.Recurrence);
                Assert.Equal(new TimeSpan(23, 7, 0), edited.ScheduledAt.TimeOfDay);
                var reopened = new TaskEditorViewModel(service, edited, false);
                Assert.Equal(23, reopened.SelectedHour); Assert.Equal(7, reopened.SelectedMinute);
                Assert.Equal(new[] { 2, 3, 4, 5, 7 }, reopened.Weekdays.Where(d => d.IsSelected).Select(d => d.Value));
                editor.UpdateLayout();
                if (screenshotDirectory is not null)
                {
                    var content = (FrameworkElement)editor.Content;
                    var dpi = VisualTreeHelper.GetDpi(content);
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
                        (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(screenshotDirectory, "task-editor-choices.png")); png.Save(file);
                }
                editor.Width = 600; editor.Height = 480;
                editor.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(1, System.Windows.Controls.Grid.GetRow((FrameworkElement)editor.FindName("ScheduleColumn")));
                var formScroll = (System.Windows.Controls.ScrollViewer)editor.FindName("FormScroll");
                Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Auto, formScroll.VerticalScrollBarVisibility);
                Assert.True(formScroll.ScrollableHeight > 0);
                formScroll.ScrollToEnd(); editor.UpdateLayout();
                saveBounds = saveButton.TransformToAncestor(editorRoot).TransformBounds(new Rect(saveButton.RenderSize));
                Assert.True(saveBounds.Top >= 0 && saveBounds.Bottom <= editorRoot.ActualHeight);
                editor.Width = 880; editor.Height = 760; editor.UpdateLayout();
                editorVm.Repeat = "農曆";
                editorVm.LunarDays[0].IsSelected = false;
                editorVm.LunarDays[29].IsSelected = true;
                await editorVm.SaveCommand.ExecuteAsync(null);
                edited = (await host.Services.GetRequiredService<ITaskRepository>().GetAllAsync()).Single(t => t.Title == "編輯器儲存驗證");
                Assert.Equal("LunarDay:15,30", edited.Recurrence);
                reopened = new TaskEditorViewModel(service, edited, true);
                Assert.Equal(new[] { 15, 30 }, reopened.LunarDays.Where(d => d.IsSelected).Select(d => d.Value));
                foreach (var day in editorVm.LunarDays) day.IsSelected = false;
                await editorVm.SaveCommand.ExecuteAsync(null);
                Assert.Equal("請至少勾選一天。", editorVm.Error);
                editor.Close();
                var alarmVm = new AlarmViewModel(Alarm(DateTime.Now) with { Level = AlarmLevels.Max }, "012-345-678", true);
                alarm = new AlarmWindow(alarmVm, AlarmLevels.Max);
                alarm.Show();
                Assert.True(alarm.Topmost); Assert.False(alarm.ShowInTaskbar); Assert.Equal(WindowStyle.None, alarm.WindowStyle);
                var firstBackground = alarm.Background;
                var frame=(System.Windows.Controls.Border)alarm.FindName("Frame");
                var firstBorder=frame.BorderBrush.ToString();
                await Task.Delay(600);
                Assert.Equal(firstBackground, alarm.Background);
                Assert.NotEqual(firstBorder,frame.BorderBrush.ToString());
                alarm.Close(); Assert.True(alarm.IsVisible);
                alarmVm.Input = "WRONG"; alarmVm.ConfirmCommand.Execute(null);
                Assert.True(alarm.IsVisible); Assert.Equal("WRONG", alarmVm.Input); Assert.NotEmpty(alarmVm.Error);
                alarmVm.Input = "012-345-678"; alarmVm.ConfirmCommand.Execute(null);
                Assert.True(await alarm.Completion); Assert.False(alarm.IsVisible);
                Assert.Empty(await host.Services.GetRequiredService<IAckLogRepository>().GetRangeAsync(DateTime.MinValue, DateTime.MaxValue));
            }
            finally { alarm?.Finish(false); window?.ForceClose(); Directory.Delete(paths.DataDirectory, true); }
        });
    }
    internal static Task RunSta(Func<Task> work)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await work(); finished.SetResult(); }
                catch (Exception ex) { finished.SetException(ex); }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    private static AlarmTask Alarm(DateTime at) => new() { Id = "timed", Title = "測試提醒", ScheduledAt = at, CreatedAt = at.AddHours(-1), UpdatedAt = at.AddHours(-1) };
    private static IHost Build(Paths paths, CapturePresenter capture) => new HostBuilder().ConfigureServices(services =>
    {
        CompositionRoot.ConfigureServices(services); services.AddSingleton<IAppPaths>(paths); services.AddSingleton<IAlarmPresenter>(capture);
    }).Build();
    private sealed class CapturePresenter : IAlarmPresenter
    {
        public TaskCompletionSource<DateTime> Fired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count;
        public Task ShowAsync(string id, AlarmTask task, DateTime scheduledAt, bool preview, CancellationToken ct = default)
        { Interlocked.Increment(ref Count); Fired.TrySetResult(DateTime.Now); return Task.CompletedTask; }
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "CloudAlarmOverlay.M1.AppTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
    }
}
