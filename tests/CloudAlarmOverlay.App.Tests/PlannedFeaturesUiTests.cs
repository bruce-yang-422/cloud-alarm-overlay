using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;
public sealed class PlannedFeaturesUiTests
{
    [Fact] public Task Sidebar_health_card_tracks_each_service_and_overall_state()=>MilestoneOneTests.RunSta(async()=>
    {
        var paths=new Paths();using var host=new HostBuilder().ConfigureServices(s=>{
            CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);
        }).Build();MainWindow? window=null;
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var vm=host.Services.GetRequiredService<MainViewModel>();await vm.InitializeAsync();
            var notified=false;vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.OverallHealth))notified=true;};
            window=host.Services.GetRequiredService<MainWindow>();window.Width=1050;window.Height=780;window.ShowInTaskbar=false;window.ShowActivated=false;window.Show();
            foreach(var dark in new[]{false,true})
            {
                CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(dark,ThemeColorStyle.Default);
                vm.LocalScheduleHealth=vm.SheetAHealth=vm.SheetBHealth=new("正常","最後檢查正常","Healthy");
                Assert.Equal("全部正常",vm.OverallHealth.Label);Assert.True(notified);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                var badge=(ContentControl)window.FindName("OverallHealthStatus");
                Assert.Equal("Healthy",((HealthStatus)badge.Content).Severity);
                Capture(window,dark?"connection-status-dark":"connection-status-light");
                vm.SheetAHealth=new("未設定","尚未設定同步來源","Pending");Assert.Equal("尚待就緒",vm.OverallHealth.Label);
                vm.SheetBHealth=new("中斷","上次同步已逾時","Stopped");Assert.Equal("部分中斷",vm.OverallHealth.Label);
                vm.LocalScheduleHealth=new("異常","排程失敗","Error");Assert.Equal("狀態異常",vm.OverallHealth.Label);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.Equal("Error",((HealthStatus)badge.Content).Severity);
                Assert.Contains("Sheet A：未設定",vm.OverallHealth.Detail);
            }
        }
        finally { CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);window?.ForceClose();host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true); }
    });
    [Fact] public Task Pomodoro_history_preserves_cards_and_list_space()=>MilestoneOneTests.RunSta(async()=>
    {
        var paths=new Paths();using var host=new HostBuilder().ConfigureServices(s=>{
            CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);
        }).Build();Window? window=null;
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var repository=host.Services.GetRequiredService<IPomodoroRepository>();
            for(var i=0;i<18;i++)await repository.SaveLogAsync(new PomodoroLogEntry {
                Id=$"history-{i}",Type=i%3==0?"Break":"Focus",Result=i%4==0?"Interrupted":"Completed",
                StartedAt=DateTime.Today.AddHours(8).AddMinutes(i*30),EndedAt=DateTime.Today.AddHours(8).AddMinutes(i*30+25),PlannedMinutes=25 });
            var vm=host.Services.GetRequiredService<PomodoroViewModel>();await vm.LoadAsync();
            var view=new PomodoroHistoryView { DataContext=vm };
            window=new Window { Content=view,Width=780,Height=650,ShowInTaskbar=false,ShowActivated=false };window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var toolbar=(FrameworkElement)view.FindName("PomodoroHistoryToolbar");
            var grid=(DataGrid)view.FindName("PomodoroHistoryGrid");
            var cards=(FrameworkElement)view.FindName("PomodoroSummaryCards");
            var dates=(Expander)view.FindName("PomodoroDateFilters");
            var phase=(ListBox)view.FindName("PomodoroPhasePicker");
            var result=(ComboBox)view.FindName("PomodoroResultPicker");
            Assert.False(dates.IsExpanded);Assert.Equal(15,vm.History.Count);Assert.True(vm.CanNext);
            foreach(var dark in new[]{false,true})
            {
                CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(dark,ThemeColorStyle.Default);
                view.Background=new SolidColorBrush(dark?Color.FromRgb(36,52,70):Colors.White);window.UpdateLayout();
                Assert.Equal(64,cards.ActualHeight);Assert.True(toolbar.ActualHeight<260,$"Toolbar: {toolbar.ActualHeight}");
                Assert.True(grid.ActualHeight>toolbar.ActualHeight,$"Grid: {grid.ActualHeight}, toolbar: {toolbar.ActualHeight}");
                Capture(window,dark?"pomodoro-history-dark":"pomodoro-history-light");
                phase.SelectedItem="專注";result.SelectedItem="完成";await vm.RefreshAsync();
                Assert.All(vm.History,row=>{Assert.Equal("專注",row.Phase);Assert.Equal("完成",row.Result);});
                Assert.True(vm.FilteredCompleted>0);Assert.Equal(0,vm.FilteredInterrupted);
                dates.IsExpanded=true;window.UpdateLayout();Assert.True(grid.ActualHeight>200);
                dates.IsExpanded=false;await vm.ClearFiltersCommand.ExecuteAsync(null);
                Assert.Equal("全部",phase.SelectedItem);Assert.Equal("全部",result.SelectedItem);
            }
        }
        finally { CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);window?.Close();host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true); }
    });
    [Fact] public Task History_toolbar_keeps_filters_and_gives_space_to_records()=>MilestoneOneTests.RunSta(async()=>
    {
        var paths=new Paths();using var host=new HostBuilder().ConfigureServices(s=>{
            CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);
        }).Build();
        Window? window=null;
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var vm=host.Services.GetRequiredService<MainViewModel>();await vm.InitializeAsync();
            var view=new TaskHistoryView { DataContext=vm };
            window=new Window { Content=view, Width=780, Height=650, ShowInTaskbar=false, ShowActivated=false };window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var toolbar=(FrameworkElement)view.FindName("HistoryToolbar");
            var grid=(DataGrid)view.FindName("HistoryGrid");
            var dates=(Expander)view.FindName("HistoryDateFilters");
            var source=(ComboBox)view.FindName("HistorySourcePicker");
            var result=(ComboBox)view.FindName("HistoryResultPicker");
            Assert.False(dates.IsExpanded);
            foreach(var dark in new[]{false,true})
            {
                CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(dark,ThemeColorStyle.Default);
                view.Background=new SolidColorBrush(dark?Color.FromRgb(36,52,70):Colors.White);
                window.UpdateLayout();
                Assert.True(toolbar.ActualHeight<185,$"Toolbar: {toolbar.ActualHeight}");
                Assert.True(grid.ActualHeight>toolbar.ActualHeight*2,$"Grid: {grid.ActualHeight}, toolbar: {toolbar.ActualHeight}");
                Capture(window,dark?"history-compact-dark":"history-compact-light");
                source.SelectedItem=TaskSources.SheetB;result.SelectedItem="稍後提醒";
                Assert.Equal(TaskSources.SheetB,vm.HistorySource);Assert.Equal("稍後提醒",vm.HistoryResult);
                dates.IsExpanded=true;window.UpdateLayout();Assert.True(grid.ActualHeight>200);
                vm.HistoryFrom=DateTime.Today.AddDays(1);dates.IsExpanded=false;window.UpdateLayout();
                Assert.NotEmpty(vm.HistoryHint);Assert.Contains(vm.HistoryFrom.ToString("yyyy/MM/dd"),vm.HistoryDateSummary);
                vm.ClearHistoryFiltersCommand.Execute(null);window.UpdateLayout();
                Assert.Equal("全部",source.SelectedItem);Assert.Equal("全部",result.SelectedItem);Assert.Empty(vm.HistoryHint);
            }
        }
        finally { CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);window?.Close();host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true); }
    });
    [Fact] public Task Task_toolbar_keeps_list_space_and_menus_follow_selection()=>MilestoneOneTests.RunSta(async()=>
    {
        var paths=new Paths();using var host=new HostBuilder().ConfigureServices(s=>{
            CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);
        }).Build();
        MainWindow? window=null;
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var vm=host.Services.GetRequiredService<MainViewModel>();await vm.InitializeAsync();
            vm.Tasks.Add(new TaskRow(TaskItem() with {Source=TaskSources.Local}));
            vm.Tasks.Add(new TaskRow(TaskItem() with {Id="cloud",Title="公司共用任務",Source=TaskSources.SheetA}));
            vm.PageIndex=1;
            window=host.Services.GetRequiredService<MainWindow>();window.Width=1050;window.Height=780;window.ShowInTaskbar=false;window.ShowActivated=false;window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var grid=(DataGrid)window.FindName("TaskGrid");
            var toolbar=(FrameworkElement)window.FindName("TaskToolbar");
            var batch=(Button)window.FindName("TaskBatchButton");
            var single=(Button)window.FindName("TaskActionsButton");
            var tools=(Button)window.FindName("TaskToolsButton");
            foreach(var dark in new[]{false,true})
            {
                CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(dark,ThemeColorStyle.Default);
                grid.UnselectAll();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                Assert.False(single.IsVisible);Assert.False(batch.IsVisible);
                Assert.True(toolbar.ActualHeight<180,$"Toolbar height: {toolbar.ActualHeight}");
                Assert.True(grid.ActualHeight>toolbar.ActualHeight*2,$"Grid: {grid.ActualHeight}, toolbar: {toolbar.ActualHeight}");
                tools.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(tools.ContextMenu.IsOpen);Assert.Same(vm.ImportTasksCommand,((MenuItem)tools.ContextMenu.Items[0]).Command);tools.ContextMenu.IsOpen=false;
                grid.SelectedItem=vm.Tasks[0];await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                Assert.True(single.IsVisible);Assert.False(batch.IsVisible);
                Capture(window,dark?"tasks-compact-dark":"tasks-compact-light");
                grid.SelectAll();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                Assert.False(single.IsVisible);Assert.True(batch.IsVisible);
                batch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Same(vm.BatchTasksCommand,((MenuItem)batch.ContextMenu.Items[0]).Command);batch.ContextMenu.IsOpen=false;
                grid.UnselectAll();grid.SelectedItem=vm.Tasks[1];
                Assert.False(vm.EditTaskCommand.CanExecute(null));Assert.True(vm.CopyTaskCommand.CanExecute(null));
            }
        }
        finally { CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);window?.Close();host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true); }
    });
    private sealed class WeatherBrowser : CloudAlarmOverlay.App.Services.IBrowserLauncher
    {
        public Uri? Opened;
        public bool Fail;
        public void Open(Uri address) { if(Fail)throw new InvalidOperationException();Opened=address; }
    }
    private sealed class SettingsDialogs : CloudAlarmOverlay.App.Services.IUserDialogs
    {
        public string? Json=AdminSettingsJson.Template;
        public Action? OnOpen;
        public int Opens;
        public string? Exported;
        public Action? OnSave;
        public bool ExportSettingsJson(string contents,Action authorize) { OnSave?.Invoke();authorize();Exported=contents;return true; }
        public bool Confirm(string message)=>true;
        public void Edit(AlarmTask? task,bool copy) { }
        public void Export(string contents) { }
        public Task<string?> OpenSettingsJsonAsync() { Opens++;OnOpen?.Invoke();return Task.FromResult(Json); }
    }
    [Fact] public Task Admin_json_command_refreshes_fields_and_rechecks_session_after_picker()=>MilestoneOneTests.RunSta(async()=>
    {
        var paths=new Paths();var dialogs=new SettingsDialogs();
        using var host=new HostBuilder().ConfigureServices(s=>{
            CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);
            s.AddSingleton<CloudAlarmOverlay.App.Services.IUserDialogs>(dialogs);
        }).Build();
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var vm=host.Services.GetRequiredService<MainViewModel>();
            await vm.ImportAdminSettingsCommand.ExecuteAsync(null);Assert.Equal(0,dialogs.Opens);
            await vm.Admin.ExportCurrentSettingsCommand.ExecuteAsync(null);Assert.Null(dialogs.Exported);
            var auth=host.Services.GetRequiredService<IAuthenticationService>();await auth.EnsureDefaultAdministratorAsync();
            Assert.True(await auth.AuthenticateAsync("admin","12345"));
            await vm.ImportAdminSettingsCommand.ExecuteAsync(null);
            Assert.Equal("REPLACE_WITH_SHEET_A_ID",vm.SheetAId);Assert.Equal(45,vm.IntervalSeconds);
            Assert.Contains("已匯入並儲存",vm.Admin.Message);
            dialogs.Json="{\"FormatVersion\":1,\"UpdateManifestUrl\":\"https://example.com/version.json\"}";
            await vm.ImportAdminSettingsCommand.ExecuteAsync(null);
            Assert.Equal("https://example.com/version.json",vm.Preferences.Maintenance.UpdateUrl);
            vm.SheetAId="unsaved-draft";
            await vm.Admin.ExportCurrentSettingsCommand.ExecuteAsync(null);
            Assert.NotNull(dialogs.Exported);Assert.Contains("REPLACE_WITH_SHEET_A_ID",dialogs.Exported);
            Assert.DoesNotContain("unsaved-draft",dialogs.Exported);
            Assert.NotEmpty(AdminSettingsJson.Parse(dialogs.Exported));
            dialogs.Exported=null;dialogs.OnSave=host.Services.GetRequiredService<AdminSession>().SignOut;
            await vm.Admin.ExportCurrentSettingsCommand.ExecuteAsync(null);Assert.Null(dialogs.Exported);
            Assert.True(await auth.AuthenticateAsync("admin","12345"));
            dialogs.Json=AdminSettingsJson.Template.Replace("REPLACE_WITH_SHEET_A_ID","changed");
            dialogs.OnOpen=host.Services.GetRequiredService<AdminSession>().SignOut;
            await vm.ImportAdminSettingsCommand.ExecuteAsync(null);
            Assert.Equal("REPLACE_WITH_SHEET_A_ID",(await host.Services.GetRequiredService<SyncConfiguration>().LoadAsync()).SheetAId);
        }
        finally { host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true); }
    });
    [Theory][InlineData(5)][InlineData(10)][InlineData(15)][InlineData(30)]
    public void Snooze_selects_interval_without_confirming(int minutes)
    {
        var vm=new AlarmViewModel(TaskItem(),"123-456-789",false,allowSnooze:true){SnoozeMinutes=minutes};
        var selected=0;var confirmed=false;vm.Snoozed+=value=>selected=value;vm.Confirmed+=()=>confirmed=true;
        vm.SnoozeCommand.Execute(null);Assert.Equal(minutes,selected);Assert.False(confirmed);
    }
    [Fact] public void Preview_pomodoro_and_exhausted_notifications_cannot_snooze()
    {
        var task=TaskItem();
        foreach(var vm in new[]{new AlarmViewModel(task with{Level=AlarmLevels.Max},"123",false,allowSnooze:true,snoozeCount:3),
            new AlarmViewModel(task,"123",true,allowSnooze:true),new AlarmViewModel(task,"123",false,"番茄鐘",true),
            new AlarmViewModel(task,"123",false,allowSnooze:true,snoozeCount:3),new AlarmViewModel(task,"123",false)})
        {Assert.False(vm.CanSnooze);var deferred=false;vm.Snoozed+=_=>deferred=true;vm.SnoozeCommand.Execute(null);Assert.False(deferred);}
        Assert.False(SnoozePolicy.CanDefer(AlarmLevels.High,0,false));Assert.True(SnoozePolicy.CanDefer(AlarmLevels.Mid,0,false));
    }
    [Theory][InlineData(5, "")][InlineData(10, "wrong")][InlineData(15, "")][InlineData(30, "wrong")]
    public void Forced_notification_can_snooze_without_code_but_confirm_still_requires_it(int minutes,string input)
    {
        Assert.True(SnoozePolicy.CanDefer(AlarmLevels.Max,0,false));
        var vm=new AlarmViewModel(TaskItem() with{Level=AlarmLevels.Max},"123-456-789",false,allowSnooze:true){SnoozeMinutes=minutes,Input=input};
        var selected=0;var confirmed=false;var incorrect=false;
        vm.Snoozed+=value=>selected=value;vm.Confirmed+=()=>confirmed=true;vm.Incorrect+=()=>incorrect=true;
        Assert.True(vm.RequiresCode);Assert.True(vm.CanSnooze);Assert.Contains("不需輸入確認碼",vm.Instruction);
        vm.SnoozeCommand.Execute(null);Assert.Equal(minutes,selected);Assert.False(confirmed);Assert.False(incorrect);
        vm.ConfirmCommand.Execute(null);Assert.False(confirmed);Assert.True(incorrect);
        vm.Input="123456789";vm.ConfirmCommand.Execute(null);Assert.True(confirmed);
    }
    [Fact] public async Task Forced_window_closes_as_deferred_without_acknowledgement()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var vm=new AlarmViewModel(TaskItem() with{Level=AlarmLevels.Max},"123-456-789",false,allowSnooze:true);
            var window=new AlarmWindow(vm,AlarmLevels.Max){WindowState=WindowState.Normal,Width=700,Height=700,ShowActivated=false};
            try
            {
                window.Show();window.UpdateLayout();
                vm.SnoozeCommand.Execute(null);
                Assert.False(await window.Completion);Assert.Equal(5,window.SnoozeMinutes);Assert.False(window.IsVisible);
            }
            finally{window.Finish(false);}
        });
    }
    [Fact] public async Task New_views_render_weather_wraps_and_header_opens_forecast()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var paths=new Paths();var browser=new WeatherBrowser();using var host=new HostBuilder().ConfigureServices(s=>{
                CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);s.AddSingleton<CloudAlarmOverlay.App.Services.IBrowserLauncher>(browser);s.AddSingleton(new HttpClient(new Handler()));
            }).Build();
            MainWindow? main=null;AlarmWindow? alarm=null;TaskImportWindow? import=null;
            try
            {
                await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
                await host.Services.GetRequiredService<IDeviceIdentityService>().SetInitialIdentityAsync("UI-TEST","測試");
                var weather=host.Services.GetRequiredService<IWeatherService>();
                await weather.SaveAsync(new(true,new("台北市",25.05,121.53)));await weather.RefreshAsync();
                var vm=host.Services.GetRequiredService<MainViewModel>();await vm.InitializeAsync();vm.UpdateClock(DateTime.Now);
                main=host.Services.GetRequiredService<MainWindow>();main.Width=1050;main.Height=780;main.ShowInTaskbar=false;main.ShowActivated=false;main.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);main.UpdateLayout();
                var card=(Button)main.FindName("WeatherCard");Assert.True(card.IsVisible);Assert.Contains("29",vm.Preferences.Weather!.Current);
                var panel=Assert.IsType<WrapPanel>(card.Parent);
                Capture(main,"home-weather");
                Assert.True(card.TranslatePoint(new Point(),panel).Y>0,$"WeatherY={card.TranslatePoint(new Point(),panel).Y}, PanelWidth={panel.ActualWidth}, MainWidth={main.ActualWidth}, WeatherWidth={card.ActualWidth}");
                foreach(var style in Enum.GetValues<ThemeColorStyle>())
                foreach(var dark in new[]{false,true})
                {
                    CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(dark,style);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);main.UpdateLayout();
                    Assert.True(card.IsVisible);Assert.True(card.ActualWidth<=panel.ActualWidth);
                    Capture(main,$"weather-{style}-{(dark?"dark":"light")}");
                }
                CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);
                vm.OpenWeatherCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);main.UpdateLayout();
                Assert.Equal(0,vm.PageIndex);Assert.Equal(vm.Preferences.Weather!.ForecastUri,browser.Opened);
                browser.Fail=true;vm.OpenWeatherCommand.Execute(null);Assert.Contains("無法開啟氣象署",vm.Status);
                vm.Preferences.SelectedSettingsTab=6;vm.PageIndex=4;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);main.UpdateLayout();Capture(main,"weather-settings");
                var csv=host.Services.GetRequiredService<LocalTaskCsvService>();
                var rows=await csv.PreviewAsync(LocalTaskCsvService.Template(DateTime.Now));
                import=new TaskImportWindow{DataContext=new TaskImportViewModel(csv,rows),ShowInTaskbar=false,ShowActivated=false};import.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);import.UpdateLayout();Capture(import,"csv-import");import.Close();
                alarm=new AlarmWindow(new AlarmViewModel(TaskItem(),"123-456-789",false,allowSnooze:true),AlarmLevels.Mid){ShowInTaskbar=false,ShowActivated=false};
                alarm.Show();alarm.UpdateLayout();Capture(alarm,"snooze-notification");
                ((AlarmViewModel)alarm.DataContext).SnoozeCommand.Execute(null);
                Assert.False(await alarm.Completion);Assert.Equal(5,alarm.SnoozeMinutes);
            }
            finally
            {
                CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);
                alarm?.Finish(false);import?.Close();main?.Close();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);
            }
        });
    }
    private static AlarmTask TaskItem()=>new(){Id="ui",Title="請確認本週工作進度",Description="忙碌時可以選擇稍後提醒。",ScheduledAt=DateTime.Now.AddDays(1),CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
    private static void Capture(Window window,string name)
    {
        var directory=Environment.GetEnvironmentVariable("CLOUD_ALARM_FEATURE_SCREENSHOT_DIR");if(directory is null)return;
        Directory.CreateDirectory(directory);var content=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
    }
    private sealed class Handler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("""
        {"current":{"time":"2026-09-24T14:00","temperature_2m":29,"apparent_temperature":32,"precipitation":0.2,"weather_code":2},"daily":{"time":["2026-09-24","2026-09-25"],"weather_code":[2,61],"temperature_2m_min":[25,24],"temperature_2m_max":[32,31],"precipitation_probability_max":[20,60]}}
        """)});
    }
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory{get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmFeatureUi",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
}
