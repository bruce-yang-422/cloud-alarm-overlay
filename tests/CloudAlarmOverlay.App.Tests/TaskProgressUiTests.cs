using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class TaskProgressUiTests
{
    [Fact] public Task Source_badges_and_selection_remain_distinct_and_readable_in_every_theme()=>MilestoneOneTests.RunSta(async()=>
    {
        using var f=new Fixture();await f.Initialize();var vm=f.Get<MainViewModel>();await vm.InitializeAsync();vm.PageIndex=1;
        var at=DateTime.Today.AddDays(3).AddHours(12);
        var local=Sample("local-design","午休",at) with{Level=AlarmLevels.Low};
        var sheetA=Sample("sheet-a-design","值日生 倒垃圾",at) with{Source=TaskSources.SheetA,ExternalId="a",Level=AlarmLevels.Mid};
        var sheetB=sheetA with{Id="sheet-b-design",Source=TaskSources.SheetB,Title="跑網路部下午報表",Level=AlarmLevels.High};
        var calendar=Sample("calendar-design","回診提醒",at) with{Source="Google:"+Guid.NewGuid().ToString("N"),ExternalId="event",Note="私人帳號 · 日曆",CalendarStartAt=at,GoogleReminderAt=at,GoogleReminderEnabled=false,Enabled=false,Level=AlarmLevels.Max};
        var privateSheet=sheetA with{Id="private-sheet",Source="Google:"+Guid.NewGuid().ToString("N"),Note="公司帳號 · 任務表",Title="私人試算表任務"};
        var secondLocal=local with{Id="local-alternate",Title="準備下班"};
        var secondCalendar=calendar with{Id="calendar-alternate",Title="下次回診"};
        vm.Tasks.Clear();foreach(var task in new[]{local,sheetA,sheetB,calendar,privateSheet,secondLocal,secondCalendar})vm.Tasks.Add(new TaskRow(task));
        var window=f.Get<MainWindow>();window.ShowActivated=false;window.ShowInTaskbar=false;window.Width=1344;window.Height=960;window.Show();
        try
        {
            var grid=(DataGrid)window.FindName("TaskGrid");
            foreach(var dark in new[]{false,true})foreach(var style in Enum.GetValues<ThemeColorStyle>())
            {
                AdaptiveBrushExtension.Apply(dark,style);grid.UnselectAll();window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var rows=vm.Tasks.Select(item=>(DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(item)).ToArray();
                var rowColors=rows.Select(row=>BrushColor(row.Background)).ToArray();
                Assert.Equal(6,rowColors.Distinct().Count());
                Assert.NotEqual(rowColors[0],rowColors[5]);Assert.NotEqual(rowColors[1],rowColors[2]);Assert.NotEqual(rowColors[3],rowColors[6]);
                Assert.Equal(rowColors[2],rowColors[4]);
                Assert.All(rows,row=>Assert.Equal(Visibility.Collapsed,Indicator(row).Visibility));
                var sourceColors=new List<Color>();
                foreach(var row in rows)
                {
                    var box=Descendants<Border>(row).Single(b=>b.Name=="SourceBox");
                    var text=Descendants<TextBlock>(box).Single(t=>t.Name=="SourceText");
                    Assert.True(Contrast(box.Background,text.Foreground)>=4.5,$"Source contrast: {dark}/{style}");
                    sourceColors.Add(BrushColor(box.Background));
                    var badge=Descendants<Border>(row).Single(b=>b.Name=="Badge");
                    var label=Descendants<TextBlock>(badge).Single(t=>t.Name=="BadgeText");
                    Assert.True(Contrast(badge.Background,label.Foreground)>=4.5,$"Level contrast: {dark}/{style}");
                }
                Assert.Equal(sourceColors[1],sourceColors[2]);Assert.Equal(sourceColors[1],sourceColors[4]);Assert.Equal(3,sourceColors.Distinct().Count());
                Assert.Contains(Descendants<TextBlock>(rows[3]),t=>t.Text=="Google 日曆");
                Assert.Contains(Descendants<TextBlock>(rows[4]),t=>t.Text=="Google Sheets");
                var locked=Descendants<CheckBox>(rows[1]).Single(c=>c.Style==window.FindResource("TaskReminderSwitch"));
                Assert.False(locked.IsEnabled);Assert.Equal(1d,locked.Opacity);
                grid.SelectedItem=vm.Tasks[1];await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                Assert.True(rows[1].IsSelected);
                Assert.NotEqual(BrushColor(rows[0].Background),BrushColor(rows[1].Background));
                Assert.Equal(3d,Indicator(rows[1]).ActualWidth);
                Assert.Equal((Color)ColorConverter.ConvertFromString(dark?"#60A5FA":"#2563EB"),BrushColor(Indicator(rows[1]).Background));
                Assert.True(Contrast(rows[1].Background,Indicator(rows[1]).Background)>=3);
                var selectedColor=BrushColor(rows[1].Background);
                grid.SelectedItems.Add(vm.Tasks[3]);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                Assert.Equal(selectedColor,BrushColor(rows[3].Background));Assert.Equal(3d,Indicator(rows[3]).ActualWidth);
                Assert.Equal(BrushColor(Indicator(rows[1]).Background),BrushColor(Indicator(rows[3]).Background));
                // Selecting a toolbar control must not remove the selection color.
                var toolbarButton=Descendants<Button>(window).First(b=>b.IsVisible&&b.IsEnabled&&b.Focusable);
                Assert.True(toolbarButton.Focus());await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(selectedColor,BrushColor(rows[1].Background));Assert.Equal(selectedColor,BrushColor(rows[3].Background));
                // Verify rendered pixels, not just row properties: system row templates can
                // ignore BorderBrush/BorderThickness even when their values are correct.
                var renderedRoot=(FrameworkElement)window.Content;
                var rendered=new RenderTargetBitmap((int)Math.Ceiling(renderedRoot.ActualWidth),(int)Math.Ceiling(renderedRoot.ActualHeight),96,96,PixelFormats.Pbgra32);
                rendered.Render(renderedRoot);
                foreach(var row in rows)
                {
                    var origin=row.TransformToAncestor(renderedRoot).Transform(new Point(0,row.ActualHeight/2));
                    var pixels=new byte[16];
                    rendered.CopyPixels(new Int32Rect((int)Math.Round(origin.X),(int)Math.Round(origin.Y),4,1),pixels,16,0);
                    var expected=BrushColor(row.IsSelected?Indicator(row).Background:row.Background);
                    for(var x=0;x<3;x++)Assert.Equal(expected,Color.FromArgb(pixels[x*4+3],pixels[x*4+2],pixels[x*4+1],pixels[x*4]));
                    if(row.IsSelected)Assert.NotEqual(expected,Color.FromArgb(pixels[15],pixels[14],pixels[13],pixels[12]));
                }
                foreach(var row in rows)
                {
                    var title=Descendants<TextBlock>(row).Single(t=>t.Text==((TaskRow)row.Item).Title);
                    Assert.True(Contrast(row.Background,title.Foreground)>=4.5);
                    var detail=Descendants<TextBlock>(row).Single(t=>t.Text==((TaskRow)row.Item).TaskDetail);
                    Assert.True(Contrast(row.Background,detail.Foreground)>=4.5);
                }
                var cell=Descendants<DataGridCell>(rows[1]).First();Assert.True(cell.Focus());window.UpdateLayout();
                Assert.NotEqual(Colors.Transparent,BrushColor(cell.BorderBrush));
                if(style==ThemeColorStyle.Default)
                {
                    foreach(var width in new[]{1050d,1344d})
                    {
                        window.Width=width;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        foreach(var row in rows)
                            Assert.All(Descendants<TextBlock>(row).Where(t=>t.Name=="SourceText"),t=>
                            {
                                var labelWidth=new FormattedText(t.Text,System.Globalization.CultureInfo.CurrentUICulture,t.FlowDirection,
                                    new Typeface(t.FontFamily,t.FontStyle,t.FontWeight,t.FontStretch),t.FontSize,t.Foreground,VisualTreeHelper.GetDpi(t).PixelsPerDip).WidthIncludingTrailingWhitespace;
                                Assert.True(t.ActualWidth>=labelWidth,$"Source label clipped: {t.Text} at {width}");
                            });
                        Capture(window,$"task-colors-{(dark?"dark":"light")}-{width}");
                    }
                }
            }
        }
        finally{AdaptiveBrushExtension.Apply(false);window.ForceClose();}
    });
    private static Border Indicator(DataGridRow row)=>Descendants<Border>(row).Single(b=>b.Name=="SelectionIndicator");
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
    private static Color BrushColor(Brush brush)=>((SolidColorBrush)brush).Color;
    private static double Contrast(Brush first,Brush second)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value){var s=value/255d;return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4);}
            return .2126*Channel(color.R)+.7152*Channel(color.G)+.0722*Channel(color.B);
        }
        var a=Luminance(BrushColor(first));var b=Luminance(BrushColor(second));return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
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
