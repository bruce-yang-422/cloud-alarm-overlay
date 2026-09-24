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
    private sealed class SettingsDialogs : CloudAlarmOverlay.App.Services.IUserDialogs
    {
        public string? Json=AdminSettingsJson.Template;
        public Action? OnOpen;
        public int Opens;
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
            var auth=host.Services.GetRequiredService<IAuthenticationService>();await auth.EnsureDefaultAdministratorAsync();
            Assert.True(await auth.AuthenticateAsync("admin","12345"));
            await vm.ImportAdminSettingsCommand.ExecuteAsync(null);
            Assert.Equal("REPLACE_WITH_SHEET_A_ID",vm.SheetAId);Assert.Equal(45,vm.IntervalSeconds);
            Assert.Contains("已匯入並儲存",vm.Admin.Message);
            dialogs.Json="{\"FormatVersion\":1,\"UpdateManifestUrl\":\"https://example.com/version.json\"}";
            await vm.ImportAdminSettingsCommand.ExecuteAsync(null);
            Assert.Equal("https://example.com/version.json",vm.Preferences.Maintenance.UpdateUrl);
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
    [Fact] public async Task New_views_render_weather_wraps_and_header_opens_weather_settings()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var paths=new Paths();using var host=new HostBuilder().ConfigureServices(s=>{
                CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);s.AddSingleton(new HttpClient(new Handler()));
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
                Assert.Equal(4,vm.PageIndex);Assert.True(vm.Preferences.SelectedSettingsTab>0);Capture(main,"weather-settings");
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
