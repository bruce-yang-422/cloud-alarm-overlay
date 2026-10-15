using System.IO;
using System.Windows;
using System.Windows.Controls;
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

public sealed class TaskProgressUiTests
{
    [Fact] public Task Completion_sections_preserve_external_sources_repeat_instances_and_undo()=>MilestoneOneTests.RunSta(async()=>
    {
        using var f=new Fixture();await f.Initialize();var tasks=f.Get<ITaskRepository>();
        var local=Sample("local","整理出貨報表",DateTime.Today.AddDays(1).AddHours(15));
        var overdue=Sample("overdue","逾期待辦",DateTime.Today.AddDays(-2));
        var routine=Sample("routine","午休",DateTime.Today.AddDays(-30).AddHours(13)) with{Recurrence="Daily"};
        var external=Sample("external","外部訂單核對",DateTime.Today.AddDays(1)) with{Source=TaskSources.SheetB,ExternalId="order"};
        var future=Sample("calendar-future","下次回診",DateTime.Today.AddDays(5)) with{Source="Google:"+Guid.NewGuid().ToString("N"),ExternalId="future",Enabled=false,CalendarStartAt=DateTime.Today.AddDays(5),GoogleReminderAt=DateTime.Today.AddDays(5),GoogleReminderEnabled=false,ActivityStartAt=DateTime.Today.AddDays(5),ActivityEndAt=DateTime.Today.AddDays(5).AddHours(1)};
        var ended=future with{Id="calendar-ended",ExternalId="ended",Title="已結束回診",ScheduledAt=DateTime.Today.AddDays(-2),CalendarStartAt=DateTime.Today.AddDays(-2),ActivityStartAt=DateTime.Today.AddDays(-2),ActivityEndAt=DateTime.Today.AddDays(-2).AddHours(1)};
        foreach(var task in new[]{local,overdue,routine})await tasks.SaveLocalAsync(task);
        await tasks.ReplaceCloudCacheAsync(external.Source,[external]);await tasks.ReplaceCloudCacheAsync(future.Source,[future,ended]);
        var vm=f.Get<MainViewModel>();await vm.InitializeAsync();vm.PageIndex=1;
        var window=f.Get<MainWindow>();window.Width=1344;window.Height=960;window.ShowInTaskbar=false;window.ShowActivated=false;
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            Assert.Equal("待處理",vm.TaskSection);Assert.DoesNotContain(vm.Tasks,r=>r.Task.Id==ended.Id);
            Assert.Equal("逾期",vm.Tasks.First(r=>r.Task.Id==overdue.Id).Progress);
            Assert.Equal("未設提醒",vm.Tasks.First(r=>r.Task.Id==future.Id).State);
            vm.SetTaskSelection(vm.Tasks.Where(r=>r.Task.Id==local.Id||r.Task.Id==external.Id));
            await vm.CompleteSelectedTasksCommand.ExecuteAsync(null);Assert.Equal(2,vm.CompletedTaskCount);
            await vm.UndoTaskCompletionCommand.ExecuteAsync(null);Assert.Equal(0,vm.CompletedTaskCount);
            var row=vm.Tasks.Single(r=>r.Task.Id==external.Id);vm.SetTaskSelection([row]);
            Assert.True(vm.CompleteSelectedTasksCommand.CanExecute(null));await vm.CompleteSelectedTasksCommand.ExecuteAsync(null);
            Assert.DoesNotContain(vm.Tasks,r=>r.Task.Id==external.Id);Assert.True(vm.CanUndoTaskCompletion);
            vm.TaskSection="已完成";Assert.Equal(external.Id,Assert.Single(vm.Tasks).Task.Id);
            Assert.Equal(external,await tasks.GetByIdAsync(external.Id));
            await tasks.ReplaceCloudCacheAsync(external.Source,[external with{UpdatedAt=DateTime.Now.AddMinutes(1)}]);await vm.RefreshCommand.ExecuteAsync(null);
            Assert.Equal(external.Id,Assert.Single(vm.Tasks).Task.Id);
            vm.SourceFilter=TaskSources.Local;Assert.Empty(vm.Tasks);Assert.Equal(0,vm.CompletedTaskCount);vm.SourceFilter="全部來源";
            await vm.UndoTaskCompletionCommand.ExecuteAsync(null);vm.TaskSection="待處理";
            Assert.Contains(vm.Tasks,r=>r.Task.Id==external.Id);
            var repeat=vm.Tasks.Single(r=>r.Task.Id==routine.Id);Assert.Equal(DateTime.Today.AddHours(13),repeat.OccurrenceAt);
            vm.SetTaskSelection([repeat]);await vm.CompleteSelectedTasksCommand.ExecuteAsync(null);
            Assert.True(vm.Tasks.Single(r=>r.Task.Id==routine.Id).OccurrenceAt>DateTime.Today.AddDays(1).AddTicks(-1));
            vm.TaskSection="全部";Assert.Equal(2,vm.Tasks.Count(r=>r.Task.Id==routine.Id));
            vm.TaskSection="已完成";var done=Assert.Single(vm.Tasks);Assert.Equal(routine.Id,done.Task.Id);
            vm.SetTaskSelection([done]);await vm.RestoreSelectedTasksCommand.ExecuteAsync(null);
            Assert.Empty(vm.Tasks);vm.TaskSection="待處理";Assert.Equal(DateTime.Today.AddHours(13),vm.Tasks.Single(r=>r.Task.Id==routine.Id).OccurrenceAt);
            vm.TaskSection="已結束";Assert.Equal(ended.Id,Assert.Single(vm.Tasks).Task.Id);Assert.Equal("已結束",vm.Tasks[0].Progress);
            vm.SetTaskSelection([vm.Tasks[0]]);await vm.CompleteSelectedTasksCommand.ExecuteAsync(null);
            Assert.Empty(vm.Tasks);vm.TaskSection="已完成";Assert.Equal(ended.Id,Assert.Single(vm.Tasks).Task.Id);
            vm.DismissTaskProgressMessageCommand.Execute(null);vm.TaskSection="待處理";
            foreach(var width in new[]{1050d,1344d})
            {
                window.Width=width;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                var grid=(DataGrid)window.FindName("TaskGrid");
                Assert.True(grid.ActualWidth>600);Assert.Contains(grid.Columns,c=>c.Header is "進度");Assert.Contains(grid.Columns,c=>c.Header is "提醒");
                Capture(window,"task-progress-pending-"+width);
            }
            vm.TaskSection="已完成";((DataGrid)window.FindName("TaskGrid")).SelectedItem=vm.Tasks[0];await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();Capture(window,"task-progress-completed");
            vm.ShowTaskCalendarCommand.Execute(null);await vm.TaskCalendar.PendingBuild;
            vm.TaskCalendar.Month=ended.ActivityStartAt!.Value.Date;await vm.TaskCalendar.PendingBuild;
            vm.TaskCalendar.SelectDayCommand.Execute(ended.ActivityStartAt.Value.Date);
            var completedEvent=Assert.Single(vm.TaskCalendar.DayEvents,e=>e.Task.Id==ended.Id);
            Assert.NotNull(completedEvent.CompletedAt);Assert.True(vm.ToggleCalendarCompletionCommand.CanExecute(completedEvent));
            await vm.ToggleCalendarCompletionCommand.ExecuteAsync(completedEvent);await vm.TaskCalendar.PendingBuild;
            vm.TaskSection="已結束";await vm.TaskCalendar.PendingBuild;
            Assert.Null(Assert.Single(vm.TaskCalendar.DayEvents,e=>e.Task.Id==ended.Id).CompletedAt);
        }
        finally{window.ForceClose();}
    });
    [Fact] public Task Acknowledging_a_notification_does_not_complete_the_business_task()=>MilestoneOneTests.RunSta(async()=>
    {
        using var f=new Fixture();await f.Initialize();var task=Sample("confirmed","已簽收仍待辦",DateTime.Today.AddHours(1));
        await f.Get<ITaskRepository>().SaveLocalAsync(task);
        await f.Get<IAckLogRepository>().SaveAsync(new(){Id="ack",TaskId=task.Id,DeviceId="test",ScheduledAt=task.ScheduledAt,TriggeredAt=task.ScheduledAt,AcknowledgedAt=task.ScheduledAt.AddMinutes(1),Result="Acknowledged"});
        var vm=f.Get<MainViewModel>();await vm.InitializeAsync();Assert.Contains(vm.Tasks,r=>r.Task.Id==task.Id&&!r.IsCompleted);
        vm.TaskSection="已完成";Assert.Empty(vm.Tasks);
    });
    private static AlarmTask Sample(string id,string title,DateTime scheduled)=>new(){Id=id,Title=title,ScheduledAt=scheduled,CreatedAt=DateTime.Today.AddDays(-40),UpdatedAt=DateTime.Now};
    private static void Capture(Window window,string name)
    {
        var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots/task-progress"));Directory.CreateDirectory(directory);
        var content=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
    }
    private sealed class Fixture:IDisposable,IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmTaskProgressUi",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        private readonly IHost host;
        public Fixture()=>host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(this);}).Build();
        public T Get<T>() where T:notnull=>host.Services.GetRequiredService<T>();
        public Task Initialize()=>Get<IDatabaseInitializer>().InitializeAsync();
        public void Dispose(){host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    }
}
