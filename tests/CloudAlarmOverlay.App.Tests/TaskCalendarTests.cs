using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Infrastructure.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;

public sealed class TaskCalendarTests
{
    private static AlarmTask Activity(string id,DateTime start,DateTime end,bool allDay=false)=>new(){Id=id,Title=id,ActivityStartAt=start,ActivityEndAt=end,ActivityAllDay=allDay,Enabled=false,ScheduledAt=start,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
    [Fact] public async Task Week_start_defaults_to_Sunday_and_all_seven_choices_persist_and_align_spanning_events()
    {
        using var f=new Fixture();await f.Initialize();var settings=f.Get<ISettingsRepository>();
        var vm=new TaskCalendarViewModel(f.Get<ITaskSchedulingService>(),settings){Month=new(2026,10,1)};
        await vm.LoadPreferencesAsync();Assert.Equal(0,vm.WeekStart);Assert.Equal("日",vm.Weekdays[0].ShortLabel);
        var activity=Activity("展覽",new(2026,10,2,10,0,0),new(2026,10,5,20,0,0));await vm.SetTasksAsync([activity]);
        foreach(var day in new[]{1,2,3,4,5,6,0})
        {
            vm.WeekStart=day;await vm.PendingBuild;await vm.PendingSave;
            Assert.Equal((DayOfWeek)day,vm.Weeks[0].Days[0].Date.DayOfWeek);Assert.Equal(day,vm.Weekdays[0].Value);
            var covered=vm.Weeks.SelectMany(w=>w.Segments.SelectMany(s=>Enumerable.Range(s.Column,s.Span).Select(i=>w.Days[i].Date))).ToArray();
            Assert.Equal(new[]{new DateTime(2026,10,2),new(2026,10,3),new(2026,10,4),new(2026,10,5)},covered);
            var reopened=new TaskCalendarViewModel(f.Get<ITaskSchedulingService>(),settings);await reopened.LoadPreferencesAsync();Assert.Equal(day,reopened.WeekStart);
        }
        await settings.SaveAsync(new(){Key=TaskCalendarViewModel.WeekStartSettingKey,Value="invalid"});await vm.LoadPreferencesAsync();Assert.Equal(0,vm.WeekStart);
    }
    [Fact] public async Task Weekends_and_loaded_holidays_are_red_but_makeup_workdays_are_explicit()
    {
        using var f=new Fixture();await f.Initialize();var holidays=f.Get<IHolidayRepository>();
        await holidays.ReplaceCacheAsync([new(){Date=new(2026,10,7),Type="國定假日",Note="測試假日"},new(){Date=new(2026,10,3),Type="補班日"}]);
        var vm=new TaskCalendarViewModel(f.Get<ITaskSchedulingService>(),null,holidays){Month=new(2026,10,1)};await vm.SetTasksAsync([]);
        var dates=vm.Weeks.SelectMany(w=>w.Days).ToDictionary(d=>d.Date);
        Assert.True(dates[new(2026,10,4)].IsRedDay);Assert.True(dates[new(2026,10,17)].IsRedDay);
        Assert.True(dates[new(2026,10,7)].IsRedDay);Assert.Contains("測試假日",dates[new(2026,10,7)].Label);
        Assert.False(dates[new(2026,10,3)].IsRedDay);Assert.Equal("補班",dates[new(2026,10,3)].DayNote);
        Assert.False(dates[new(2026,10,6)].IsRedDay);
    }
    [Fact] public async Task Cross_week_and_month_ranges_include_last_day_but_not_exclusive_end()
    {
        using var f=new Fixture();await f.Initialize();var vm=new TaskCalendarViewModel(f.Get<ITaskSchedulingService>()){WeekStart=1,Month=new(2026,10,1)};
        var prescription=Activity("領藥",new(2026,10,1),new(2026,10,11),true);
        var exhibition=Activity("寵物展",new(2026,10,2,10,0,0),new(2026,10,5,20,0,0));
        var crossMonth=Activity("跨月",new(2026,9,30,10,0,0),new(2026,10,2));
        await vm.SetTasksAsync([prescription,exhibition,crossMonth]);Assert.Equal(5,vm.Weeks.Count);
        var days=vm.Weeks.SelectMany(w=>w.Days).ToArray();Assert.Contains(days.Single(d=>d.Date==new DateTime(2026,10,10)).Events,e=>e.Task.Id==prescription.Id);
        Assert.Empty(days.Single(d=>d.Date==new DateTime(2026,10,11)).Events);
        Assert.DoesNotContain(days.Single(d=>d.Date==new DateTime(2026,10,2)).Events,e=>e.Task.Id==crossMonth.Id);
        var bars=vm.Weeks.SelectMany(w=>w.Segments).Where(s=>s.Entry.Task.Id==exhibition.Id).ToArray();Assert.Equal(2,bars.Length);Assert.Equal(3,bars[0].Span);Assert.True(bars[0].ContinuesAfter);Assert.True(bars[1].ContinuesBefore);Assert.Equal(1,bars[1].Span);
        vm.SelectDayCommand.Execute(new DateTime(2026,10,5));Assert.Equal(2,vm.DayEvents.Count);
        vm.SelectEventCommand.Execute(vm.DayEvents.Single(e=>e.Task.Id==exhibition.Id));Assert.Equal(new DateTime(2026,10,5),vm.SelectedDay);
        vm.NextMonthCommand.Execute(null);await vm.PendingBuild;Assert.Contains("沒有",vm.Message);
    }
    [Fact] public async Task Weekly_reminders_remain_visible_while_daily_routines_are_hidden_and_overflow_is_accessible()
    {
        using var f=new Fixture();await f.Initialize();var vm=new TaskCalendarViewModel(f.Get<ITaskSchedulingService>()){WeekStart=1,Month=new(2026,10,1)};
        var daily=new AlarmTask{Id="weekly",Title="每週五",ScheduledAt=new(2026,10,1,9,0,0),Recurrence="Weekly:5",Enabled=false,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
        var tasks=Enumerable.Range(1,5).Select(i=>Activity("活動"+i,new(2026,10,2),new(2026,10,3),true)).Append(daily).Concat(new[]{"Daily","Weekly:1,2,3,4,5","Weekly:7,6,5,4,3,2,1","Weekly:5,4,3,2,1"}.Select((rule,i)=>daily with{Id="routine"+i,Recurrence=rule,Enabled=true})).ToArray();
        await vm.SetTasksAsync(tasks);vm.SelectDayCommand.Execute(new DateTime(2026,10,2));Assert.Equal(6,vm.DayEvents.Count);
        Assert.Equal(4,vm.DayRoutines.Count);
        Assert.Contains(vm.Weeks.SelectMany(w=>w.Days),d=>d.Date==new DateTime(2026,10,2)&&d.More.Contains("6"));
        Assert.All(vm.Weeks.SelectMany(w=>w.Segments),s=>Assert.InRange(s.Lane,0,2));
        Assert.DoesNotContain(vm.Weeks.SelectMany(w=>w.Segments),s=>s.Entry.Task.Id.StartsWith("routine"));
        Assert.NotNull(await f.Get<ITaskSchedulingService>().GetNextOccurrenceAsync(tasks.Single(t=>t.Id=="routine0"),new DateTime(2026,10,1)));
        Assert.True(tasks.Single(t=>t.Id=="routine0").Enabled);
        vm.SelectDayCommand.Execute(new DateTime(2026,10,4));Assert.Equal(2,vm.DayRoutines.Count);
        await vm.SetTasksAsync([daily]);Assert.Empty(vm.DayRoutines);Assert.All(vm.Weeks.SelectMany(w=>w.Segments),s=>Assert.Equal("weekly",s.Entry.Task.Id));
    }
    [Fact] public async Task Almanac_and_daily_work_respect_holidays_and_makeup_days()
    {
        using var f=new Fixture();await f.Initialize();var holidays=f.Get<IHolidayRepository>();var lunar=f.Get<ILunarCalendarRepository>();
        await holidays.ReplaceCacheAsync([new(){Date=new(2026,10,7),Type="國定假日",Note="測試假日"},new(){Date=new(2026,10,3),Type="補班日"}]);
        await lunar.ReplaceCacheAsync([new(){Date=new(2026,10,8),LunarDay=28,SolarTerm="寒露"}]);
        var vm=new TaskCalendarViewModel(f.Get<ITaskSchedulingService>(),null,holidays,lunar){Month=new(2026,10,1)};
        var work=new AlarmTask{Id="weekday",Title="工作日任務",ScheduledAt=new(2026,10,1,13,0,0),Recurrence="Weekly:1,2,3,4,5",SkipOnHoliday=true,Enabled=true,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
        await vm.SetTasksAsync([work]);var days=vm.Weeks.SelectMany(w=>w.Days).ToDictionary(d=>d.Date);
        Assert.Equal("八月二十",days[new(2026,9,30)].LunarDate);Assert.Equal("二十",days[new(2026,9,30)].LunarLabel);
        Assert.Equal("測試假日",days[new(2026,10,7)].HolidayName);Assert.Equal("寒露",days[new(2026,10,8)].SolarTerm);
        vm.SelectDayCommand.Execute(new DateTime(2026,10,3));Assert.Single(vm.DayRoutines);Assert.Contains("補班日",vm.DayAlmanac);
        vm.SelectDayCommand.Execute(new DateTime(2026,10,7));Assert.Empty(vm.DayRoutines);
        vm.SelectDayCommand.Execute(new DateTime(2026,10,8));Assert.Single(vm.DayRoutines);Assert.Contains("寒露",vm.DayAlmanac);Assert.Empty(vm.DayEvents);
        Assert.Empty(vm.Weeks.SelectMany(w=>w.Segments));
    }
    [Fact] public void Google_preserves_timed_and_all_day_end_independently_of_reminders()
    {
        var source=new GoogleSource{Id="test",IncludeAllDay=true};
        var allDay=GoogleCalendarProjection.Project(JsonSerializer.SerializeToElement(new{id="a",start=new{date="2026-10-01"},end=new{date="2026-10-11"}}),source,"個人","Asia/Taipei",DateTimeOffset.Now)!;
        Assert.True(allDay.ActivityAllDay);Assert.Equal(new DateTime(2026,10,11),allDay.ActivityEndAt);Assert.False(allDay.Enabled);
        var timed=GoogleCalendarProjection.Project(JsonSerializer.SerializeToElement(new{id="b",start=new{dateTime="2026-10-02T10:00:00+08:00"},end=new{dateTime="2026-10-05T20:00:00+08:00"},reminders=new{useDefault=false,overrides=new[]{new{method="popup",minutes=30}}}}),source,"個人","Asia/Taipei",DateTimeOffset.Now)!;
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T20:00:00+08:00").LocalDateTime,timed.ActivityEndAt);Assert.Equal(timed.ActivityStartAt!.Value.AddMinutes(-30),timed.ScheduledAt);
    }
    [Fact] public Task Editor_saves_inclusive_dates_and_calendar_view_renders_and_selects_entries()=>MilestoneOneTests.RunSta(async()=>
    {
        using var f=new Fixture();await f.Initialize();var editor=new TaskEditorViewModel(f.Get<ITaskService>(),null,false,activityDate:new DateTime(2026,10,1))
        {TaskTitle="慢性處方箋領藥",ActivityAllDay=true,ActivityEndDate=new(2026,10,10)};
        Assert.False(editor.Enabled);await editor.SaveCommand.ExecuteAsync(null);Assert.Equal("",editor.Error);
        var task=Assert.Single(await f.Get<ITaskRepository>().GetAllAsync());Assert.Equal(new DateTime(2026,10,11),task.ActivityEndAt);
        var reopened=new TaskEditorViewModel(f.Get<ITaskService>(),task,false);Assert.Equal(new DateTime(2026,10,10),reopened.ActivityEndDate);
        await f.Get<ITaskRepository>().SaveLocalAsync(Activity("寵物展",new(2026,10,2,10,0,0),new(2026,10,5,20,0,0)));
        await f.Get<ITaskRepository>().SaveLocalAsync(new(){Id="routine",Title="午休",ScheduledAt=new(2026,10,1,13,0,0),Recurrence="Weekly:1,2,3,4,5",Enabled=true,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now});
        var main=f.Get<MainViewModel>();await main.InitializeAsync();main.PageIndex=1;main.TaskCalendar.Month=new(2026,10,1);main.ShowTaskCalendarCommand.Execute(null);await main.TaskCalendar.PendingBuild;
        var window=f.Get<MainWindow>();window.Height=900;
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var view=Descendants(window).OfType<TaskCalendarView>().Single();Assert.True(view.IsVisible);Assert.False(((DataGrid)window.FindName("TaskGrid")).IsVisible);
            Assert.Null(view.FindName("WeekStartPicker"));
            Assert.Same(main.TaskCalendar,main.Preferences.Calendar);
            main.PageIndex=4;main.Preferences.SelectedPage=SettingsPage.Appearance;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var preferencesView=Descendants(window).OfType<PreferencesView>().Single();
            var picker=(ComboBox)preferencesView.FindName("CalendarWeekStartPicker");Assert.Equal(0,picker.SelectedValue);
            Assert.Contains(Descendants(picker).OfType<TextBlock>(),t=>t.Text=="星期日");
            picker.SelectedValue=1;await main.TaskCalendar.PendingBuild;await main.TaskCalendar.PendingSave;Assert.Equal(DayOfWeek.Monday,main.TaskCalendar.Weeks[0].Days[0].Date.DayOfWeek);
            picker.SelectedValue=0;await main.TaskCalendar.PendingBuild;await main.TaskCalendar.PendingSave;
            main.PageIndex=1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var weekend=Descendants(view).OfType<Button>().First(b=>b.DataContext is CalendarDay d&&d.InMonth&&d.IsRedDay);
            Assert.Equal(Color.FromRgb(196,60,69),((SolidColorBrush)weekend.Foreground).Color);
            var button=Descendants(view).OfType<Button>().First(b=>b.DataContext is CalendarSegment s&&s.Entry.Title=="寵物展");button.Command.Execute(button.CommandParameter);
            Assert.Equal("寵物展",main.TaskCalendar.Selected!.Title);
            Assert.Single(main.TaskCalendar.DayRoutines);Assert.True(((StackPanel)view.FindName("DayDetails")).IsVisible);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var routine=Descendants(view).OfType<Button>().First(b=>b.DataContext is CalendarEntry e&&e.Task.Id=="routine");routine.Command.Execute(routine.CommandParameter);
            Assert.Equal("午休",main.TaskCalendar.Selected!.Title);Assert.True(((StackPanel)view.FindName("DayDetails")).IsVisible);
            // The side card only lists the day's items; details open in the preview window on double-click.
            Assert.Null(view.FindName("EventDetails"));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            Assert.True(Descendants(view).OfType<Button>().Single(b=>b.DataContext is CalendarEntry e&&e.Task.Id=="routine").Tag is true);
            Assert.All(Descendants(view).OfType<Button>().Where(b=>b.DataContext is CalendarEntry e&&e.Task.Id!="routine"),b=>Assert.False(b.Tag is true));
            var previewEntry=main.TaskCalendar.DayEvents.Single(e=>e.Title=="寵物展");
            var preview=new TaskPreviewWindow(new(previewEntry.Task),previewEntry.EditLabel,canCopy:true){ShowActivated=false,ShowInTaskbar=false};
            try
            {
                preview.Show();preview.UpdateLayout();
                var edit=(Button)preview.FindName("EditButton");Assert.True(edit.IsVisible);Assert.Equal("編輯活動／提醒",edit.Content);
                Assert.True(((Button)preview.FindName("CopyButton")).IsVisible);
                Capture(preview,"calendar-preview");
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(TaskPreviewAction.Edit,preview.Action);
            }
            finally{preview.Close();}
            var readOnly=new TaskPreviewWindow(new(previewEntry.Task)){ShowActivated=false,ShowInTaskbar=false};
            try{readOnly.Show();readOnly.UpdateLayout();Assert.False(((Button)readOnly.FindName("EditButton")).IsVisible);Assert.Equal(TaskPreviewAction.None,readOnly.Action);}
            finally{readOnly.Close();}
            main.TaskCalendar.CloseDetailsCommand.Execute(null);
            foreach(var width in new[]{1050,1440})foreach(var dark in new[]{false,true})
            {
                window.Width=width;AdaptiveBrushExtension.Apply(dark);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                var selectedDate=Assert.Single(Descendants(view).OfType<Button>(),b=>b.DataContext is CalendarDay && b.Tag is true);
                Assert.Equal(main.TaskCalendar.SelectedDay,((CalendarDay)selectedDate.DataContext).Date);
                Assert.True(((Border)selectedDate.Template.FindName("SelectionFrame",selectedDate)).IsVisible);
                Assert.False(selectedDate.IsKeyboardFocused);
                Capture(window,$"calendar-{width}-"+(dark?"dark":"light"));
            }
            main.TaskCalendar.SelectDayCommand.Execute(new DateTime(2026,10,10));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var selectedHoliday=Assert.Single(Descendants(view).OfType<Button>(),b=>b.DataContext is CalendarDay && b.Tag is true);
            Assert.True(((CalendarDay)selectedHoliday.DataContext).IsRedDay);
            Assert.Equal(Color.FromRgb(255,159,168),((SolidColorBrush)selectedHoliday.Foreground).Color);
            var todayButton=Descendants(view).OfType<Button>().Single(b=>Equals(b.Content,"今天"));
            todayButton.Command.Execute(null);await main.TaskCalendar.PendingBuild;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var monthScroll=(ScrollViewer)view.FindName("MonthScroll");monthScroll.ScrollToTop();window.UpdateLayout();
            todayButton.Command.Execute(null);todayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var selectedToday=Assert.Single(Descendants(view).OfType<Button>(),b=>b.DataContext is CalendarDay && b.Tag is true);
            Assert.Equal(DateTime.Today,((CalendarDay)selectedToday.DataContext).Date);
            Assert.True(((Border)selectedToday.Template.FindName("SelectionFrame",selectedToday)).IsVisible);
            var todayTop=selectedToday.TranslatePoint(new Point(),monthScroll).Y;
            Assert.InRange(todayTop,-1,monthScroll.ViewportHeight-selectedToday.ActualHeight+1);
            Capture(window,"calendar-today-selected");
            main.TaskCalendar.Month=new(2026,10,1);await main.TaskCalendar.PendingBuild;
            main.Search="領藥";await main.TaskCalendar.PendingBuild;Assert.Empty(main.TaskCalendar.DayRoutines);Assert.All(main.TaskCalendar.Weeks.SelectMany(w=>w.Segments),s=>Assert.Equal(task.Id,s.Entry.Task.Id));
            main.ShowTaskListCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.True(((DataGrid)window.FindName("TaskGrid")).IsVisible);
        }
        finally{window.Close();AdaptiveBrushExtension.Apply(false);}
        var dialog=new TaskEditorWindow{DataContext=reopened,ShowActivated=false,ShowInTaskbar=false};
        try{dialog.Show();dialog.UpdateLayout();Capture(dialog,"activity-editor");}finally{dialog.Close();}
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject n){for(var i=0;i<VisualTreeHelper.GetChildrenCount(n);i++){var c=VisualTreeHelper.GetChild(n,i);yield return c;foreach(var x in Descendants(c))yield return x;}}
    private static void Capture(Window window,string name)
    {
        var dir=Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR");if(dir is null)return;Directory.CreateDirectory(dir);var content=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(dir,name+".png"));png.Save(file);
    }
    private sealed class Fixture:IDisposable,IAppPaths
    {
        public string DataDirectory{get;}=Path.Combine(Path.GetTempPath(),"TaskCalendarTests",Guid.NewGuid().ToString("N"));public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        private readonly IHost host;public Fixture()=>host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(this);}).Build();
        public T Get<T>()where T:notnull=>host.Services.GetRequiredService<T>();public Task Initialize()=>Get<IDatabaseInitializer>().InitializeAsync();
        public void Dispose(){host.Dispose();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    }
}
