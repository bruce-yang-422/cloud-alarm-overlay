using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;
public sealed class PomodoroUiTests
{
    [Fact] public async Task Hosted_timer_runs_without_a_main_window_and_shutdown_records_interruption()
    {
        var paths=new Paths();var clock=new Clock();
        using var host=new HostBuilder().ConfigureServices(c=>{CompositionRoot.ConfigureServices(c);c.AddSingleton<IAppPaths>(paths);c.AddSingleton<TimeProvider>(clock);}).Build();
        try
        {
            await host.StartAsync();var timer=host.Services.GetRequiredService<IPomodoroService>();await timer.InitializeAsync();
            await timer.SaveOptionsAsync(new(){FocusMinutes=5,SoundEnabled=false});await timer.StartAsync();clock.Advance(5);
            for(var i=0;i<30&&timer.State.Status=="Running";i++)await Task.Delay(100);
            Assert.Equal("AwaitingConfirmation",timer.State.Status);
            await timer.ConfirmAsync();Assert.Equal("Break",timer.State.Phase);
            clock.Advance(1);await host.StopAsync();
            var logs=await host.Services.GetRequiredService<IPomodoroRepository>().GetLogsAsync(DateTime.MinValue,DateTime.MaxValue);
            Assert.Equal(2,logs.Count);Assert.All(logs,x=>Assert.False(x.IsActive));
            var interrupted=Assert.Single(logs,x=>x.Type=="Break");Assert.Equal("Interrupted",interrupted.Result);Assert.NotNull(interrupted.EndedAt);
        }
        finally{await host.StopAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
    }
    private sealed class Clock:TimeProvider
    {
        private long elapsed;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Interlocked.Read(ref elapsed);
        public override DateTimeOffset GetUtcNow()=>new DateTimeOffset(2026,9,15,0,0,0,TimeSpan.Zero).AddTicks(GetTimestamp());
        public void Advance(int minutes)=>Interlocked.Add(ref elapsed,TimeSpan.FromMinutes(minutes).Ticks);
    }
    [Fact] public async Task Page_history_progress_and_export_use_only_completed_focus_data()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var paths=new Paths();var dialogs=new Dialogs();
            using var host=new HostBuilder().ConfigureServices(collection=>{CompositionRoot.ConfigureServices(collection);collection.AddSingleton<IAppPaths>(paths);collection.AddSingleton<IUserDialogs>(dialogs);}).Build();
            var services=host.Services;MainWindow? window=null;
            try
            {
                await services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
                var main=services.GetRequiredService<MainViewModel>();await main.InitializeAsync();
                window=services.GetRequiredService<MainWindow>();window.ShowInTaskbar=false;window.ShowActivated=false;window.Show();
                main.PageIndex=3;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Contains("番茄鐘",main.Pages);Assert.True(main.Pomodoro.CanEdit);
                Assert.Equal("25:00",main.Pomodoro.Clock);Assert.Equal(7,main.Pomodoro.Days.Count);
                Screenshot(window,"pomodoro-idle");
                var repo=services.GetRequiredService<IPomodoroRepository>();
                for(var i=0;i<17;i++)await repo.SaveLogAsync(new(){Id=i.ToString(),Type="Focus",StartedAt=DateTime.Today.AddHours(8).AddMinutes(i),CompletedAt=DateTime.Today.AddHours(9),EndedAt=DateTime.Today.AddHours(9),PlannedMinutes=25,Result="Completed"});
                await repo.SaveLogAsync(new(){Id="break",Type="Break",StartedAt=DateTime.Today,PlannedMinutes=5,Result="Interrupted",EndedAt=DateTime.Now});
                await repo.SaveLogAsync(new(){Id="active",Type="Focus",StartedAt=DateTime.Now,PlannedMinutes=25,Result="Interrupted",IsActive=true});
                await main.Pomodoro.RefreshAsync();Assert.Equal(17,main.Pomodoro.Completed);
                Assert.Equal(15,main.Pomodoro.History.Count);Assert.True(main.Pomodoro.CanNext);
                main.Pomodoro.NextPageCommand.Execute(null);Assert.Equal(3,main.Pomodoro.History.Count);
                await main.Pomodoro.ExportCommand.ExecuteAsync(null);Assert.Equal(20,dialogs.ExportText.Split("\r\n").Length);
                Assert.DoesNotContain("active",dialogs.ExportText);
                main.Pomodoro.PhaseFilter="短休息";
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);await main.Pomodoro.RefreshAsync();
                Assert.Single(main.Pomodoro.History);Assert.Equal(1,main.Pomodoro.Page);
                main.HistoryTabIndex=1;main.PageIndex=2;window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Screenshot(window,"pomodoro-history");
                var historyView=Find<PomodoroHistoryView>(window)!;var datePicker=Find<DatePicker>(historyView)!;
                datePicker.IsDropDownOpen=true;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(((System.Windows.Controls.Primitives.Popup)datePicker.Template.FindName("PART_Popup",datePicker)).IsOpen);datePicker.IsDropDownOpen=false;
                main.PageIndex=3;window.Width=1050;window.Height=680;window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Screenshot(window,"pomodoro-small");
                var view=Find<PomodoroView>(window)!;
                Assert.True(((StackPanel)view.FindName("SettingsPanel")).IsVisible);Assert.Null(Find<Expander>(view));window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var button=(Button)view.FindName("ToggleButton");
                var bottom=button.TransformToAncestor(view).Transform(new Point(0,button.ActualHeight));
                Assert.InRange(bottom.Y,0,view.ActualHeight);Assert.True(button.IsVisible);
                Screenshot(window,"pomodoro-small-settings");
                Assert.Empty(main.History);
            }
            finally{window?.ForceClose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
        });
    }
    [Fact] public async Task Medium_task_and_pomodoro_notifications_stack_and_require_confirmation()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var now=DateTime.Now;
            var task=new AlarmTask{Id="test",Title="任務提醒",ScheduledAt=now,CreatedAt=now,UpdatedAt=now};
            var firstVm=new AlarmViewModel(task,"",false);
            var secondVm=new AlarmViewModel(task with{Title="專注時間結束，休息一下吧"},"",false,"番茄鐘");
            var first=new AlarmWindow(firstVm,AlarmLevels.Mid);var second=new AlarmWindow(secondVm,AlarmLevels.Mid);
            try
            {
                first.Show();second.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(first.Topmost);Assert.True(second.Topmost);
                Assert.False(first.Left==second.Left&&first.Top==second.Top);
                Assert.False(second.Completion.IsCompleted);Assert.Equal("番茄鐘",secondVm.Caption);
                secondVm.ConfirmCommand.Execute(null);Assert.True(await second.Completion);
                firstVm.ConfirmCommand.Execute(null);Assert.True(await first.Completion);
            }finally{if(first.IsVisible)first.Finish(false);if(second.IsVisible)second.Finish(false);}
        });
    }
    private static T? Find<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)return typed;if(Find<T>(child) is {} found)return found;}return null;}
    private static void Screenshot(Window window,string name)
    {
        var directory=Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR");if(directory is null)return;
        window.UpdateLayout();Directory.CreateDirectory(directory);
        var content=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));
        bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(directory,name+".png"));encoder.Save(stream);
    }
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmPomodoroUI",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
    private sealed class Dialogs:IUserDialogs
    {
        public string ExportText="";
        public bool Confirm(string message)=>true;
        public void Edit(AlarmTask? task,bool copy){}
        public void Export(string contents)=>ExportText=contents;
        public void ExportNamed(string contents,string filename)=>ExportText=contents;
    }
}
