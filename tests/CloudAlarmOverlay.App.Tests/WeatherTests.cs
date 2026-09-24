using System.Net;
using System.Net.Http;
using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Infrastructure;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Tests;
public sealed class WeatherTests
{
    private const string Forecast = """
        {"current":{"time":"2026-09-24T14:00","temperature_2m":29.4,"apparent_temperature":32.1,"precipitation":0.2,"weather_code":2},
        "daily":{"time":["2026-09-24","2026-09-25"],"weather_code":[2,61],"temperature_2m_min":[25,24],"temperature_2m_max":[32,31],"precipitation_probability_max":[20,60]}}
        """;
    private static readonly WeatherLocation Taipei = new("台北市",25.05,121.53);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026,9,24,6,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private sealed class Settings : ISettingsRepository,IAdminSettingsStore
    {
        private readonly Dictionary<string,Setting> entries=[];
        public Task<Setting?> GetAsync(string key,CancellationToken cancellationToken=default)=>Task.FromResult(entries.GetValueOrDefault(key));
        public Task SaveAsync(Setting setting,CancellationToken cancellationToken=default){entries[setting.Key]=setting;return Task.CompletedTask;}
        public async Task SaveAsync(IReadOnlyList<Setting> settings,CancellationToken ct=default){foreach(var s in settings)await SaveAsync(s,ct);}
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public string Json=Forecast;
        public bool Fail;
        public bool Hang;
        public Uri? LastUri;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);LastUri=request.RequestUri;Started.TrySetResult();
            if(Hang)await Task.Delay(Timeout.Infinite,ct);
            if(Fail)throw new HttpRequestException("offline");
            return new(HttpStatusCode.OK){Content=new StringContent(Json)};
        }
    }
    [Fact] public async Task Parses_current_and_daily_values_with_correct_units_and_escaped_location_query()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var client=new OpenMeteoWeatherClient(http);var clock=new Clock();
        var result=await client.FetchAsync(Taipei,clock.Now,default);
        Assert.Equal(29.4,result.Temperature);Assert.Equal(32.1,result.ApparentTemperature);Assert.Equal(0.2,result.Precipitation);
        Assert.Equal(24,result.Tomorrow.Minimum);Assert.Equal(31,result.Tomorrow.Maximum);Assert.Equal(60,result.Tomorrow.RainProbability);
        Assert.Contains("forecast_days=2",handler.LastUri!.Query);Assert.Contains("timezone=Asia%2FTaipei",handler.LastUri.Query);
        handler.Json="""{"results":[{"name":"台北","admin1":"台灣","country":"台灣","latitude":25.05,"longitude":121.53}]}""";
        var city=Assert.Single(await client.SearchAsync("台北 & a",default));Assert.Equal("台北 · 台灣",city.Name);Assert.Contains("%26",handler.LastUri.Query);
    }
    [Theory][InlineData(0,"晴天")][InlineData(2,"局部多雲")][InlineData(48,"霧")][InlineData(65,"下雨")][InlineData(75,"降雪")][InlineData(99,"雷雨伴隨冰雹")][InlineData(999,"未知天氣")]
    public void Wmo_codes_have_chinese_descriptions(int code,string expected)=>Assert.Equal(expected,WeatherCodes.Describe(code).Description);
    [Fact] public async Task No_location_or_disabled_weather_makes_no_requests_including_search()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);using var weather=new WeatherService(new Settings(),new(http),new Clock());
        await weather.RefreshAsync();Assert.Equal(0,handler.Calls);
        await weather.SaveAsync(new(false,Taipei));await weather.RefreshAsync(true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>weather.SearchAsync("台北"));Assert.Equal(0,handler.Calls);
    }
    [Fact] public async Task Refresh_is_throttled_for_thirty_minutes_and_force_refreshes_after_resume()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var clock=new Clock();using var weather=new WeatherService(new Settings(),new(http),clock);
        await weather.SaveAsync(new(true,Taipei));await weather.RefreshAsync();await weather.RefreshAsync();Assert.Equal(1,handler.Calls);
        clock.Now=clock.Now.AddMinutes(29);await weather.RefreshAsync();Assert.Equal(1,handler.Calls);
        clock.Now=clock.Now.AddMinutes(1);await weather.RefreshAsync();Assert.Equal(2,handler.Calls);
        await weather.RefreshAsync(true);Assert.Equal(3,handler.Calls);
    }
    [Fact] public async Task Failure_keeps_last_success_persists_across_restart_and_changing_city_drops_old_cache()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var settings=new Settings();var clock=new Clock();
        using var weather=new WeatherService(settings,new(http),clock);await weather.SaveAsync(new(true,Taipei));await weather.RefreshAsync();
        var cached=weather.Snapshot;Assert.False(weather.IsStale);handler.Fail=true;clock.Now=clock.Now.AddMinutes(31);await weather.RefreshAsync();
        Assert.Same(cached,weather.Snapshot);Assert.True(weather.IsStale);
        using var restarted=new WeatherService(settings,new(http),clock);await restarted.LoadAsync();Assert.Equal(cached,restarted.Snapshot);Assert.True(restarted.IsStale);
        await restarted.SaveAsync(new(true,new("高雄",22.6,120.3)));Assert.Null(restarted.Snapshot);
    }
    [Fact] public async Task Malformed_or_incomplete_json_fails_without_exposing_partial_data()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);using var weather=new WeatherService(new Settings(),new(http),new Clock());
        await weather.SaveAsync(new(true,Taipei));await weather.RefreshAsync();var cached=weather.Snapshot;
        foreach(var bad in new[]{"invalid","{}",Forecast.Replace("[25,24]","[25]")})
        {handler.Json=bad;await weather.RefreshAsync(true);Assert.Same(cached,weather.Snapshot);Assert.True(weather.IsStale);}
    }
    [Fact] public async Task Disabling_cancels_an_inflight_request_and_prevents_later_connections()
    {
        using var handler=new Handler{Hang=true};using var http=new HttpClient(handler);using var weather=new WeatherService(new Settings(),new(http),new Clock());
        await weather.SaveAsync(new(true,Taipei));var pending=weather.RefreshAsync();await handler.Started.Task;
        await weather.SaveAsync(new(false,Taipei));await pending;await weather.RefreshAsync(true);Assert.Equal(1,handler.Calls);Assert.Null(weather.Snapshot);
    }
    [Fact] public async Task Http_request_has_a_ten_second_deadline()
    {
        using var handler=new Handler{Hang=true};using var http=new HttpClient(handler);var client=new OpenMeteoWeatherClient(http);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.FetchAsync(Taipei,DateTimeOffset.UtcNow,default));
        Assert.InRange(watch.Elapsed.TotalSeconds,9,15);
    }
    [Fact] public async Task Company_default_is_used_until_a_personal_location_is_saved()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var settings=new Settings();
        await settings.SaveAsync(new Setting{Key="WeatherDefaultLocation",Value=JsonSerializer.Serialize(Taipei)});
        using var weather=new WeatherService(settings,new(http),new Clock());await weather.LoadAsync();Assert.Equal(Taipei,weather.EffectiveLocation);
        var personal=new WeatherLocation("高雄",22.6,120.3);await weather.SaveAsync(new(true,personal));Assert.Equal(personal,weather.EffectiveLocation);
        await weather.SaveAsync(new(true));Assert.Equal(Taipei,weather.EffectiveLocation);
    }
    [Fact] public async Task Expired_cache_is_hidden_and_unconfigured_weather_links_to_settings()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var settings=new Settings();var clock=new Clock();
        using var weather=new WeatherService(settings,new(http),clock);var vm=new WeatherViewModel(weather,settings,clock);
        await vm.LoadAsync();Assert.Equal("設定天氣地點",vm.Current);
        await weather.SaveAsync(new(true,Taipei));await weather.RefreshAsync();
        Assert.Contains("29.4",vm.Current);Assert.Contains("0.2 mm",vm.Current);Assert.Contains("60%",vm.Tomorrow);
        clock.Now=clock.Now.AddHours(6);vm.Tick();
        Assert.Equal("天氣資料暫不可用",vm.Current);Assert.Equal("",vm.Tomorrow);Assert.True(vm.Stale);
    }
    [Fact] public void Taiwan_menu_has_all_counties_and_unique_valid_districts()
    {
        Assert.Equal(22,TaiwanWeatherLocations.Counties.Count);
        Assert.Equal(368,TaiwanWeatherLocations.All.Count);
        Assert.Equal(368,TaiwanWeatherLocations.All.Select(d=>d.Code).Distinct().Count());
        foreach(var county in TaiwanWeatherLocations.Counties) Assert.Contains(TaiwanWeatherLocations.All,d=>d.County==county);
        foreach(var district in TaiwanWeatherLocations.All)
        {
            district.Location.Validate();
            Assert.Matches("^[0-9]{3}$",district.PostalCode);
            Assert.False(string.IsNullOrWhiteSpace(district.ReferenceName));
            Assert.StartsWith("https://",district.ReferenceSource);
            Assert.InRange(district.Latitude,21,27);Assert.InRange(district.Longitude,118,123);
        }
        Assert.Equal(29,TaiwanWeatherLocations.All.Count(d=>d.County=="新北市"));
        Assert.Equal(12,TaiwanWeatherLocations.All.Count(d=>d.County=="臺北市"));
        Assert.Equal(4,TaiwanWeatherLocations.All.Count(d=>d.County=="連江縣"));
    }
    [Fact] public async Task County_district_selection_saves_offline_and_clears_previous_county_district()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var settings=new Settings();var clock=new Clock();
        using var weather=new WeatherService(settings,new(http),clock);var vm=new WeatherViewModel(weather,settings,clock);
        await vm.LoadAsync();vm.SelectedCounty="新北市";
        vm.SelectedDistrict=vm.Districts.Single(d=>d.District=="板橋區");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("新北市／板橋區",weather.Options.Location!.Name);Assert.Equal(0,handler.Calls);
        await vm.LoadAsync();Assert.Equal("板橋區",vm.SelectedDistrict!.District);
        vm.SelectedCounty="臺北市";Assert.Null(vm.SelectedDistrict);Assert.Null(vm.SelectedLocation);
        Assert.All(vm.Districts,d=>Assert.Equal("臺北市",d.County));
        await vm.SaveCommand.ExecuteAsync(null);Assert.Contains("請選擇鄉鎮市區",vm.Message);
        Assert.Equal("新北市／板橋區",weather.Options.Location.Name);
        vm.SelectedDistrict=vm.Districts.Single(d=>d.District=="中正區");
        await vm.SaveCompanyDefaultCommand.ExecuteAsync(null);
        await vm.UseDefaultCommand.ExecuteAsync(null);
        Assert.Null(weather.Options.Location);Assert.Null(vm.SelectedCounty);
        Assert.Equal("臺北市／中正區",weather.EffectiveLocation!.Name);Assert.Equal(0,handler.Calls);
    }
    [Fact] public async Task Legacy_location_is_preserved_until_a_district_is_selected()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var settings=new Settings();var clock=new Clock();
        using var weather=new WeatherService(settings,new(http),clock);
        var old=new WeatherLocation("新北市 · 臺北市 · 台灣",25.01,121.46);
        await weather.SaveAsync(new(true,old));var vm=new WeatherViewModel(weather,settings,clock);
        await vm.LoadAsync();Assert.Null(vm.SelectedCounty);Assert.Equal(old,vm.SelectedLocation);
        vm.Enabled=false;await vm.SaveCommand.ExecuteAsync(null);Assert.Equal(old,weather.Options.Location);
        Assert.False(weather.Options.Enabled);Assert.Equal(0,handler.Calls);
    }
    [Theory]
    [InlineData("板橋", "新北市", "板橋區")]
    [InlineData("新北 板橋", "新北市", "板橋區")]
    [InlineData("新北板橋", "新北市", "板橋區")]
    [InlineData("台北 大安", "臺北市", "大安區")]
    [InlineData("臺北市／大安區", "臺北市", "大安區")]
    [InlineData("220", "新北市", "板橋區")]
    [InlineData("２２０", "新北市", "板橋區")]
    [InlineData("22001", "新北市", "板橋區")]
    [InlineData("220-001", "新北市", "板橋區")]
    public void Local_search_accepts_names_aliases_and_postal_prefixes(string query,string county,string district)
    {
        var result=Assert.Single(TaiwanWeatherLocations.Search(query));
        Assert.Equal(county,result.County);Assert.Equal(district,result.District);
    }
    [Theory][InlineData("")][InlineData("   ")][InlineData("2200")][InlineData("2200000")][InlineData("999")][InlineData("不存在的地區")]
    public void Empty_or_invalid_queries_do_not_guess_a_location(string query)=>Assert.Empty(TaiwanWeatherLocations.Search(query));
    [Fact] public async Task Shared_postal_codes_and_duplicate_names_require_explicit_selection_without_network()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var settings=new Settings();var clock=new Clock();
        using var weather=new WeatherService(settings,new(http),clock);var vm=new WeatherViewModel(weather,settings,clock);
        await vm.LoadAsync();vm.LocationQuery="300";
        Assert.Equal(3,vm.SearchResults.Count);Assert.All(vm.SearchResults,d=>Assert.Equal("新竹市",d.County));
        vm.ChooseUniqueLocationCommand.Execute(null);Assert.Null(vm.SelectedDistrict);
        vm.SelectedSearchResult=vm.SearchResults.Single(d=>d.District=="東區");
        Assert.Equal("新竹市",vm.SelectedCounty);Assert.Equal("東區",vm.SelectedDistrict!.District);
        vm.LocationQuery="中正";Assert.True(vm.SearchResults.Count>1);Assert.Null(vm.SelectedSearchResult);
        Assert.Equal("新竹市",vm.SelectedCounty); // Typing never silently changes a selection.
        vm.LocationQuery="220001";vm.ChooseUniqueLocationCommand.Execute(null);
        Assert.Equal("新北市",vm.SelectedCounty);Assert.Equal("板橋區",vm.SelectedDistrict!.District);
        vm.ClearLocationQueryCommand.Execute(null);Assert.False(vm.HasLocationQuery);Assert.Empty(vm.SearchResults);
        Assert.Equal("板橋區",vm.SelectedDistrict.District);
        await vm.SaveCommand.ExecuteAsync(null);Assert.Equal("新北市／板橋區",weather.Options.Location!.Name);
        Assert.Equal(0,handler.Calls);
    }
    [Fact] public Task Picker_bindings_keep_search_and_two_level_selection_in_sync()=>MilestoneOneTests.RunSta(async()=>
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var settings=new Settings();var clock=new Clock();
        using var weather=new WeatherService(settings,new(http),clock);var vm=new WeatherViewModel(weather,settings,clock);await vm.LoadAsync();
        var picker=new CloudAlarmOverlay.App.Views.WeatherLocationPicker{DataContext=vm};
        var window=new System.Windows.Window{Content=picker,Width=620,Height=620,ShowInTaskbar=false,ShowActivated=false};
        window.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary{Source=new Uri("/CloudAlarmOverlay.App;component/Styles/LightTheme.xaml",UriKind.Relative)});
        try
        {
            window.Show();window.UpdateLayout();
            var query=(System.Windows.Controls.TextBox)picker.FindName("QueryBox");query.Text="600";
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var results=(System.Windows.Controls.ListBox)picker.FindName("ResultsList");Assert.Equal(2,results.Items.Count);
            results.SelectedItem=vm.SearchResults.Single(d=>d.District=="西區");
            Assert.Equal("嘉義市",vm.SelectedCounty);Assert.Equal("西區",vm.SelectedDistrict!.District);
            var screenshotDirectory=Environment.GetEnvironmentVariable("CLOUD_ALARM_WEATHER_PICKER_SCREENSHOT_DIR");
            foreach(var dark in new[]{false,true})
            {
                CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(dark,ThemeColorStyle.Default);
                window.Background=new System.Windows.Media.SolidColorBrush(CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Map(System.Windows.Media.Color.FromRgb(247,250,254),dark,dark?CloudAlarmOverlay.Core.Models.ThemeMode.Dark:CloudAlarmOverlay.Core.Models.ThemeMode.Light));
                picker.Foreground=new System.Windows.Media.SolidColorBrush(dark?System.Windows.Media.Colors.White:System.Windows.Media.Colors.Black);
                picker.Background=window.Background;
                window.UpdateLayout();
                var selectedPresenter=FindText(picker).ToArray();
                Assert.Contains("西區（600）",selectedPresenter);
                Assert.DoesNotContain(selectedPresenter,text=>text.Contains("TaiwanWeatherDistrict"));
                Assert.True(results.ActualHeight>0);Assert.True(results.ActualWidth<=picker.ActualWidth);
                if(screenshotDirectory is not null)
                {
                    System.IO.Directory.CreateDirectory(screenshotDirectory);
                    var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)picker.ActualWidth,(int)picker.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(picker);
                    using var file=System.IO.File.Create(System.IO.Path.Combine(screenshotDirectory,dark?"picker-dark.png":"picker-light.png"));
                    CloudAlarmOverlay.App.Services.CountdownShareRenderer.WritePng(image,file);
                }
            }
            var county=(System.Windows.Controls.ComboBox)picker.FindName("CountyPicker");county.SelectedItem="新北市";
            Assert.Null(vm.SelectedDistrict);Assert.Null(results.SelectedItem);
            var districts=(System.Windows.Controls.ComboBox)picker.FindName("DistrictPicker");Assert.Equal(29,districts.Items.Count);
            districts.SelectedItem=vm.Districts.Single(d=>d.District=="烏來區");
            Assert.Equal("新北市／烏來區",vm.SelectedLocation!.Name);Assert.Equal(0,handler.Calls);
        }
        finally{window.Close();CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);}
    });
    private static IEnumerable<string> FindText(System.Windows.DependencyObject root)
    {
        if(root is System.Windows.Controls.TextBlock text) yield return text.Text;
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var value in FindText(System.Windows.Media.VisualTreeHelper.GetChild(root,i))) yield return value;
    }
    [Fact] public void Mountain_district_reference_is_the_inhabited_office_area()
    {
        var wulai=TaiwanWeatherLocations.All.Single(d=>d.County=="新北市" && d.District=="烏來區");
        Assert.InRange(wulai.Latitude,24.85,24.88);Assert.InRange(wulai.Longitude,121.54,121.57);
        var heping=TaiwanWeatherLocations.All.Single(d=>d.County=="臺中市" && d.District=="和平區");
        Assert.InRange(heping.Latitude,24.16,24.20);Assert.InRange(heping.Longitude,120.87,120.90);
    }
    [Fact] public async Task Saved_menu_name_reloads_new_reference_coordinates_without_changing_legacy_names()
    {
        using var handler=new Handler();using var http=new HttpClient(handler);var settings=new Settings();var clock=new Clock();
        using var weather=new WeatherService(settings,new(http),clock);
        var old=new WeatherLocation("新北市／烏來區",24.783433,121.520235);await weather.SaveAsync(new(true,old));
        var vm=new WeatherViewModel(weather,settings,clock);await vm.LoadAsync();
        Assert.Equal("烏來區",vm.SelectedDistrict!.District);Assert.Equal(old,weather.Options.Location);
        await vm.SaveCommand.ExecuteAsync(null);Assert.Equal(vm.SelectedDistrict.Location,weather.Options.Location);
        Assert.NotEqual(old,weather.Options.Location);Assert.Equal(0,handler.Calls);
    }
}
