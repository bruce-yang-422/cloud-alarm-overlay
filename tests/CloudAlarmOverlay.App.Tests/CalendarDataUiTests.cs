using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Controls;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.BackgroundServices;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class CalendarDataUiTests
{
    [Fact] public async Task Startup_loads_bundled_data_offline_and_settings_control_manual_updates()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var fixture=new Fixture();
            await fixture.Get<IEnumerable<IHostedService>>().OfType<DatabaseInitializationService>().Single().StartAsync(default);
            var calendar=fixture.Get<CalendarDataService>();var state=await calendar.GetAsync();
            Assert.Equal(730,state.Lunar!.Entries.Length);Assert.Equal(0,fixture.Source.Calls);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,"Data/Calendar/SheetA_Holidays.json")));
            await fixture.Get<IDeviceIdentityService>().SetInitialIdentityAsync("CAL","日曆測試",UsageModes.Personal);
            var main=fixture.Get<MainViewModel>();var window=fixture.Get<MainWindow>();
            try
            {
                await main.InitializeAsync();window.ShowActivated=false;window.ShowInTaskbar=false;window.Width=1200;window.Height=920;window.Show();
                main.PageIndex=5;main.Admin.SelectedTab=5;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var vm=main.Admin.CalendarData;await vm.LoadAsync();Assert.False(vm.Enabled);
                Assert.Contains("bruce-yang-422/cloud-alarm-overlay/main/data/calendar",vm.HolidaysUrl);
                var view=Find<CalendarDataView>(window)!;var frequencies=(SlidingSegmentedControl)view.FindName("UpdateFrequency");
                var urlLock=(CheckBox)view.FindName("SourceUrlsLock");
                var holidays=(TextBox)view.FindName("HolidaysSource");var lunar=(TextBox)view.FindName("LunarSource");
                var restore=(Button)view.FindName("RestoreSourceUrls");
                Assert.True(urlLock.IsChecked);Assert.True(holidays.IsReadOnly);Assert.True(lunar.IsReadOnly);
                Assert.Equal(new[]{"每天","每週","每月","手動更新"},frequencies.Items.Cast<string>());
                await vm.UpdateCommand.ExecuteAsync(null);Assert.Contains("允許網路更新",vm.Message);Assert.Equal(0,fixture.Source.Calls);
                urlLock.IsChecked=false;
                Assert.False(holidays.IsReadOnly);Assert.False(lunar.IsReadOnly);
                holidays.Text="https://raw.githubusercontent.com/example/calendar/main/holidays.json";
                lunar.Text="https://raw.githubusercontent.com/example/calendar/main/lunar.json";
                await vm.SaveCommand.ExecuteAsync(null);
                Assert.True(holidays.IsReadOnly);Assert.True(lunar.IsReadOnly);
                var customOptions=(await calendar.GetAsync()).Options;
                Assert.Equal(holidays.Text,customOptions.HolidaysUrl);Assert.Equal(lunar.Text,customOptions.LunarUrl);
                urlLock.IsChecked=false;await vm.LoadAsync();Assert.True(urlLock.IsChecked);
                urlLock.IsChecked=false;holidays.Text="";lunar.Text="";
                vm.Enabled=true;frequencies.SelectedItem="手動更新";
                restore.Command.Execute(restore.CommandParameter);
                Assert.Equal(new CalendarUpdateOptions().HolidaysUrl,holidays.Text);
                Assert.Equal(new CalendarUpdateOptions().LunarUrl,lunar.Text);
                Assert.True(urlLock.IsChecked);Assert.True(holidays.IsReadOnly);Assert.True(lunar.IsReadOnly);
                Assert.True(vm.Enabled);Assert.Equal("手動更新",vm.Frequency);
                Assert.Equal(customOptions,(await calendar.GetAsync()).Options);Assert.Equal(0,fixture.Source.Calls);
                await vm.SaveCommand.ExecuteAsync(null);
                Assert.True((await calendar.GetAsync()).Options.Enabled);Assert.Equal("Manual",(await calendar.GetAsync()).Options.Frequency);
                fixture.Source.Holidays=JsonSerializer.Serialize(state.Holidays! with{Version="ui-test"},CalendarJson.Options);
                fixture.Source.Lunar=JsonSerializer.Serialize(state.Lunar with{Version="ui-test"},CalendarJson.Options);
                await calendar.UpdateAsync(false);Assert.Equal(0,fixture.Source.Calls);
                await vm.UpdateCommand.ExecuteAsync(null);Assert.Equal(2,fixture.Source.Calls);Assert.Contains("更新成功",vm.Message);Assert.Contains("ui-test",vm.Summary);
                foreach(var mode in new[]{false,true})
                {
                    AdaptiveBrushExtension.Apply(mode);window.UpdateLayout();Find<ScrollViewer>(view)!.ScrollToBottom();await Task.Delay(180);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(window,mode?"calendar-data-dark":"calendar-data-light");
                }
                window.Width=1050;window.Height=700;window.UpdateLayout();Capture(window,"calendar-data-small");
            }
            finally{AdaptiveBrushExtension.Apply(false);window.ForceClose();}
        });
    }
    [Theory]
    [InlineData(200,"application/json",true)]
    [InlineData(404,"application/json",false)]
    [InlineData(200,"text/html",false)]
    public async Task Transport_converts_github_blob_and_rejects_web_pages_and_errors(int status,string mediaType,bool success)
    {
        using var handler=new Handler((HttpStatusCode)status,mediaType);using var source=new GitHubCalendarDataSource(new HttpClient(handler));
        var task=source.DownloadAsync("https://github.com/owner/repo/blob/main/file.json");
        if(success)Assert.Equal("{}",await task);else await Assert.ThrowsAnyAsync<Exception>(()=>task);
        Assert.Equal("https://raw.githubusercontent.com/owner/repo/main/file.json",handler.Url);
    }
    private static T? Find<T>(DependencyObject parent) where T:DependencyObject
    {
        if(parent is T match)return match;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)if(Find<T>(VisualTreeHelper.GetChild(parent,i)) is {} found)return found;
        return null;
    }
    private static void Capture(Window window,string name)
    {
        var folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots"));Directory.CreateDirectory(folder);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
    }
    private sealed class Handler(HttpStatusCode status,string mediaType):HttpMessageHandler
    {
        public string? Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Url=request.RequestUri!.ToString();return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent("{}",System.Text.Encoding.UTF8,mediaType)});}
    }
    private sealed class Source:ICalendarDataSource
    {
        public int Calls;public string Holidays="",Lunar="";
        public Task<string> DownloadAsync(string url,CancellationToken ct=default){Calls++;return Task.FromResult(url.EndsWith("SheetA_Holidays.json")?Holidays:Lunar);}
    }
    private sealed class Fixture:IDisposable,IAppPaths
    {
        public string DataDirectory{get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmCalendarUi",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        public Source Source{get;}=new();
        private readonly IHost host;
        public Fixture()=>host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(this);s.AddSingleton<ICalendarDataSource>(Source);}).Build();
        public T Get<T>() where T:notnull=>host.Services.GetRequiredService<T>();
        public void Dispose(){host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    }
}
