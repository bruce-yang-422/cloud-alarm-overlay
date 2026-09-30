using System.Net;
using System.Net.Http;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Infrastructure;

namespace CloudAlarmOverlay.App.Tests;

public sealed class WeatherAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(8));
    private static string Feed(params string[] entries) =>
        "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:cap=\"urn:oasis:names:tc:emergency:cap:1.1\">" + string.Concat(entries) + "</feed>";
    private static string Entry(string id = "a", string title = "高溫", string author = "中央氣象署",
        string status = "Actual", string type = "Alert", string expires = "2026/9/30 下午 05:00:00",
        string effective = "2026/9/30 上午 07:31:00") => $"""
        <entry><id>{id}</id><title>{title}</title><updated>2026-09-30T07:32:34+08:00</updated>
        <author><name>{author}</name></author><summary type="html">{SecurityElement.Escape("<b>新北市</b> 請注意。")}</summary>
        <link rel="alternate" href="https://alerts.ncdr.nat.gov.tw/Capstorage/Tests/{id}.cap"/>
        <cap:status>{status}</cap:status><cap:msgType>{type}</cap:msgType>
        <cap:effective>{effective}</cap:effective><cap:expires>{expires}</cap:expires></entry>
        """;

    [Fact] public void Official_feed_filters_status_source_and_period_and_handles_taiwan_dates()
    {
        var entries = CwaAlertClient.Parse(Feed(Entry(), Entry(), Entry("b", "地震"),
            Entry("test", status: "Test"), Entry("exercise", status: "Exercise"), Entry("cancel", type: "Cancel"),
            Entry("other", author: "台灣自來水公司"), Entry("expired", expires: "2026/9/30 上午 09:59:59"),
            Entry("future", effective: "2026/9/30 下午 01:00:00")), Now);
        Assert.Equal(2, entries.Count);
        Assert.Equal(WeatherAlertKind.Earthquake, entries[0].Kind);
        Assert.Equal(TimeSpan.FromHours(8), entries[1].ExpiresAt.Offset);
        Assert.Equal(17, entries[1].ExpiresAt.Hour);
        Assert.Equal("新北市 請注意。", entries[1].Summary);
        Assert.Empty(CwaAlertClient.Parse(Feed(Entry()), Now.AddHours(7)));
    }

    [Theory]
    [InlineData("地震", WeatherAlertKind.Earthquake)]
    [InlineData("颱風", WeatherAlertKind.Typhoon)]
    [InlineData("海嘯", WeatherAlertKind.Tsunami)]
    [InlineData("豪大雨", WeatherAlertKind.Rain)]
    [InlineData("大雷雨即時訊息", WeatherAlertKind.Thunderstorm)]
    [InlineData("陸上強風", WeatherAlertKind.Wind)]
    [InlineData("高溫", WeatherAlertKind.Heat)]
    [InlineData("低溫", WeatherAlertKind.Cold)]
    [InlineData("濃霧", WeatherAlertKind.Fog)]
    [InlineData("其他資訊", WeatherAlertKind.Other)]
    public void Alert_types_have_distinct_valid_vector_icons(string title, WeatherAlertKind kind)
    {
        Assert.Equal(kind, CwaAlertClient.Classify(title));
        var badge = new WeatherAlertBadge(kind, title, "");
        Assert.False(Geometry.Parse(badge.IconPath).IsEmpty());
        Assert.Equal("https", badge.OfficialUri.Scheme);
        Assert.EndsWith("cwa.gov.tw", badge.OfficialUri.Host);
    }

    [Fact] public void Broken_xml_or_dates_fail_instead_of_being_reported_as_no_alerts()
    {
        Assert.Throws<FormatException>(() => CwaAlertClient.Parse("<html/>", Now));
        Assert.Throws<FormatException>(() => CwaAlertClient.Parse(Feed(Entry(expires: "?")), Now));
        Assert.ThrowsAny<Exception>(() => CwaAlertClient.Parse("<!DOCTYPE feed [<!ENTITY bad SYSTEM 'file:///secret'>]><feed>&bad;</feed>", Now));
        Assert.Empty(CwaAlertClient.Parse(Feed(), Now));
    }

    [Fact] public async Task Alerts_refresh_independently_and_failure_expiry_and_disabling_hide_badges()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var clock = new Clock(); var settings = new Settings();
        using var weather = new WeatherService(settings, new(http), clock, new(http));
        var browser = new Browser(); var vm = new WeatherViewModel(weather, settings, clock, browser);
        await weather.RefreshAsync(); Assert.Equal(0, handler.AlertCalls);
        await weather.SaveAsync(new(true, new("新北市／五股區", 25.08, 121.43)));
        await weather.RefreshAsync(); vm.Tick();
        Assert.Single(vm.AlertBadges); Assert.False(weather.IsStale); Assert.False(weather.AlertsStale);
        Assert.Contains("篩選地點：新北市／五股區", vm.AlertBadges[0].Details);
        vm.OpenAlertCommand.Execute(vm.AlertBadges[0]); Assert.Equal(vm.AlertBadges[0].OfficialUri, browser.Opened);
        await weather.RefreshAsync(); Assert.Equal(1, handler.AlertCalls);
        clock.Now = clock.Now.AddMinutes(5); await weather.RefreshAsync();
        Assert.Equal(2, handler.AlertCalls); Assert.Equal(1, handler.ForecastCalls);
        handler.FailAlerts = true; clock.Now = clock.Now.AddMinutes(5); await weather.RefreshAsync(); vm.Tick();
        Assert.True(weather.AlertsStale); Assert.Empty(vm.AlertBadges); Assert.Contains("暫不可用", vm.AlertStatus);
        Assert.False(weather.IsStale);
        handler.FailAlerts = false; handler.Alerts = Feed(Entry(expires: "2026/9/30 上午 10:16:00"));
        clock.Now = clock.Now.AddMinutes(5); await weather.RefreshAsync(); vm.Tick(); Assert.Single(vm.AlertBadges);
        clock.Now = clock.Now.AddMinutes(1); vm.Tick(); Assert.Empty(vm.AlertBadges); Assert.Empty(vm.AlertStatus);
        handler.Alerts = Feed(Entry()); await weather.RefreshAsync(true); vm.Tick(); Assert.Single(vm.AlertBadges);
        clock.Now = clock.Now.AddMinutes(10); vm.Tick(); Assert.Empty(vm.AlertBadges); Assert.True(vm.HasAlertStatus);
        await weather.SaveAsync(new(false, weather.EffectiveLocation)); await weather.RefreshAsync(true); vm.Tick();
        Assert.Null(weather.Alerts); Assert.Empty(vm.AlertBadges); Assert.Empty(vm.AlertStatus);
        Assert.Equal(5, handler.AlertCalls);
    }

    [Fact] public async Task Successful_empty_feed_removes_old_alerts_and_alert_failure_does_not_block_forecast()
    {
        using var handler = new Handler { FailAlerts = true }; using var http = new HttpClient(handler);
        var clock = new Clock(); var settings = new Settings();
        using var weather = new WeatherService(settings, new(http), clock, new(http));
        await weather.SaveAsync(new(true, new("新北市", 25.08, 121.43)));
        await weather.RefreshAsync(); Assert.NotNull(weather.Snapshot); Assert.True(weather.AlertsStale);
        handler.FailAlerts = false; await weather.RefreshAsync(true); Assert.Single(weather.Alerts!.Items);
        handler.Alerts = Feed(); await weather.RefreshAsync(true); Assert.Empty(weather.Alerts!.Items); Assert.False(weather.AlertsStale);
    }

    [Fact] public async Task Disabling_weather_cancels_an_inflight_alert_request()
    {
        using var handler = new Handler { HangAlerts = true }; using var http = new HttpClient(handler);
        using var weather = new WeatherService(new Settings(), new(http), new Clock(), new(http));
        await weather.SaveAsync(new(true, new("新北市", 25, 121)));
        var pending = weather.RefreshAsync(); await handler.Started.Task;
        await weather.SaveAsync(new(false)); await pending;
        Assert.Null(weather.Alerts); Assert.Equal(0, handler.ForecastCalls);
    }

    [Fact] public Task Compact_badges_wrap_and_render_in_both_themes() => MilestoneOneTests.RunSta(async () =>
    {
        using var handler = new Handler { Alerts = Feed(Entry("eq", "地震"), Entry("ty", "颱風"), Entry("rain", "豪大雨"), Entry()) };
        using var http = new HttpClient(handler); var settings = new Settings(); var clock = new Clock();
        using var weather = new WeatherService(settings, new(http), clock, new(http));
        await weather.SaveAsync(new(true, new("新北市", 25, 121))); await weather.RefreshAsync();
        var vm = new WeatherViewModel(weather, settings, clock); vm.Tick();
        var view = new WeatherAlertsView { DataContext = vm, Width = 260 };
        var window = new Window { Content = view, Width = 300, Height = 210, ShowInTaskbar = false, ShowActivated = false };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/CloudAlarmOverlay.App;component/Styles/LightTheme.xaml", UriKind.Relative) });
        try
        {
            window.Show(); window.UpdateLayout();
            var items = (ItemsControl)view.FindName("Badges"); Assert.Equal(4, items.Items.Count);
            foreach (var dark in new[] { false, true })
            {
                CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(dark, ThemeColorStyle.Default);
                window.Background = new SolidColorBrush(dark ? Color.FromRgb(32, 34, 38) : Colors.White);
                window.UpdateLayout();
                var buttons = Descendants<Button>(view).Where(b => b.IsVisible).ToArray(); Assert.Equal(4, buttons.Length);
                Assert.All(buttons, b => { Assert.InRange(b.ActualWidth, 40, 110); Assert.InRange(b.ActualHeight, 28, 40); });
                Assert.True(buttons[3].TranslatePoint(new Point(), view).Y > buttons[0].TranslatePoint(new Point(), view).Y);
                var directory = Environment.GetEnvironmentVariable("CLOUD_ALARM_WEATHER_ALERT_SCREENSHOTS");
                if (directory is not null)
                {
                    System.IO.Directory.CreateDirectory(directory);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(300, 210, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                    using var file = System.IO.File.Create(System.IO.Path.Combine(directory, dark ? "weather-alerts-dark.png" : "weather-alerts-light.png"));
                    CountdownShareRenderer.WritePng(bitmap, file);
                }
            }
        }
        finally { window.Close(); CloudAlarmOverlay.App.Styles.AdaptiveBrushExtension.Apply(false, ThemeColorStyle.Default); }
    });

    private static string Area(string name, string? code = null) => $"<area><areaDesc>{name}</areaDesc>" +
        (code is null ? "" : $"<geocode><valueName>Taiwan_Geocode_103</valueName><value>{code}</value></geocode>") + "</area>";
    private static string Info(string areas, string title = "高溫", string severity = "") => $"""
        <info><language>zh-TW</language><headline>{title}</headline><description>新北市、臺北市高溫資訊，影響地區以 area 為準。</description>
        <effective>2026-09-30T07:00:00+08:00</effective><expires>2026-09-30T17:00:00+08:00</expires>
        <parameter><valueName>severity_level</valueName><value>{severity}</value></parameter>{areas}</info>
        """;
    private static string Cap(string id, params string[] infos) => "\uFEFF" + $"""
        <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2"><identifier>{id}</identifier><status>Actual</status>
        <msgType>Alert</msgType><scope>Public</scope>{string.Concat(infos)}</alert>
        """;
    private static WeatherLocation Wugu => TaiwanWeatherLocations.All.Single(d => d.County == "新北市" && d.District == "五股區").Location;
    private static WeatherAlert Parsed(string area) => Assert.Single(CwaAlertClient.ParseCap(Cap("a", Info(area)),
        Assert.Single(CwaAlertClient.Parse(Feed(Entry()), Now)), Now));

    [Theory]
    [InlineData("新北市五股區", "6501500", AlertAreaMatch.Matches)]
    [InlineData("新北市五股區", "65000150", AlertAreaMatch.Matches)]
    [InlineData("新北市", "65", AlertAreaMatch.Matches)]
    [InlineData("新北市", "65000", AlertAreaMatch.Matches)]
    [InlineData("新北市", "6500600", AlertAreaMatch.Outside)]
    [InlineData("新北市", "63", AlertAreaMatch.Outside)]
    [InlineData("新北市五股區", null, AlertAreaMatch.Matches)]
    [InlineData("新北市新店區", null, AlertAreaMatch.Outside)]
    [InlineData("新北市", null, AlertAreaMatch.Matches)]
    [InlineData("臺北市", null, AlertAreaMatch.Outside)]
    [InlineData("新北市山區", null, AlertAreaMatch.Unknown)]
    [InlineData("北部海面", null, AlertAreaMatch.Unknown)]
    [InlineData("全臺", null, AlertAreaMatch.Matches)]
    [InlineData("新北市", "unrecognized", AlertAreaMatch.Unknown)]
    public void Region_filter_uses_official_codes_not_county_names_in_the_summary(string name, string? code, AlertAreaMatch expected)
        => Assert.Equal(expected, WeatherAlertRegion.Match(Parsed(Area(name, code)), Wugu));

    [Fact] public void Cap_info_blocks_keep_different_levels_and_areas_separate()
    {
        var source = Assert.Single(CwaAlertClient.Parse(Feed(Entry()), Now));
        var alerts = CwaAlertClient.ParseCap(Cap("a", Info(Area("新北市新店區", "6500600"), severity: "高溫橙色36燈號"),
            Info(Area("新北市五股區", "6501500"), severity: "高溫黃色燈號")), source, Now);
        Assert.Equal(2, alerts.Count);
        var local = Assert.Single(alerts, a => WeatherAlertRegion.Match(a, Wugu) == AlertAreaMatch.Matches);
        var badge = WeatherAlertBadge.Create(new[] { local }.GroupBy(a => a.Kind).Single(), Now, Wugu);
        Assert.Contains("高溫黃色燈號", badge.Details); Assert.DoesNotContain("橙色", badge.Details);
        Assert.Contains("符合範圍：新北市五股區", badge.Details);
        Assert.Throws<FormatException>(() => CwaAlertClient.ParseCap(Cap("wrong", Info(Area("全臺"))), source, Now));
        Assert.Empty(CwaAlertClient.ParseCap(Cap("a", Info(Area("全臺"))).Replace("<msgType>Alert", "<msgType>Cancel"), source, Now));
        Assert.Empty(CwaAlertClient.ParseCap(Cap("a", Info(Area("全臺"))).Replace("<status>Actual", "<status>Test"), source, Now));
        var mixedCodes = Parsed(Area("新北市", "65").Replace("</area>",
            "<geocode><valueName>Taiwan_Geocode_103</valueName><value>6500600</value></geocode></area>"));
        Assert.Equal(AlertAreaMatch.Outside, WeatherAlertRegion.Match(mixedCodes, Wugu));
        var unknownScheme = Parsed(Area("新北市", "65").Replace("Taiwan_Geocode_103", "unknown-scheme"));
        Assert.Equal(AlertAreaMatch.Unknown, WeatherAlertRegion.Match(unknownScheme, Wugu));
    }

    [Fact] public void Earthquake_uses_reported_county_intensities_not_the_epicentre_location()
    {
        var alert = Parsed("<area><areaDesc>震央</areaDesc><circle>23.748,121.813 0.000</circle></area>" + Area("最大震度3級地區", "10015"));
        Assert.Equal(AlertAreaMatch.Outside, WeatherAlertRegion.Match(alert, Wugu));
        var hualien = TaiwanWeatherLocations.All.First(d => d.County == "花蓮縣").Location;
        Assert.Equal(AlertAreaMatch.Matches, WeatherAlertRegion.Match(alert, hualien));
    }

    [Fact] public void District_aliases_county_only_locations_and_geographic_areas_are_explicit()
    {
        var taipei = TaiwanWeatherLocations.All.Single(d => d.County == "臺北市" && d.District == "內湖區").Location;
        Assert.Equal(AlertAreaMatch.Matches, WeatherAlertRegion.Match(Parsed(Area("台北市內湖區")), taipei with { Name = "台北市/內湖區" }));
        var districtOnly = Parsed(Area("新北市新店區", "6500600"));
        Assert.Equal(AlertAreaMatch.Matches, WeatherAlertRegion.Match(districtOnly, new("新北市", 25, 121)));
        Assert.Equal(AlertAreaMatch.Unknown, WeatherAlertRegion.Match(districtOnly, new("舊地點不明", 25, 121)));
        var polygon = Parsed("<area><areaDesc>大雷雨範圍</areaDesc><polygon>24,120 26,120 26,123 24,123 24,120</polygon></area>");
        Assert.Equal(AlertAreaMatch.Matches, WeatherAlertRegion.Match(polygon, Wugu));
        Assert.Equal(AlertAreaMatch.Matches, WeatherAlertRegion.Match(polygon, new("邊界", 24, 121)));
        Assert.Equal(AlertAreaMatch.Outside, WeatherAlertRegion.Match(polygon, new("外部", 23, 121)));
        var circle = Parsed("<area><areaDesc>範圍</areaDesc><circle>25,121 10</circle></area>");
        Assert.Equal(AlertAreaMatch.Matches, WeatherAlertRegion.Match(circle, new("中心", 25, 121)));
        Assert.Equal(AlertAreaMatch.Outside, WeatherAlertRegion.Match(circle, new("外部", 24, 121)));
        Assert.Equal(AlertAreaMatch.Unknown, WeatherAlertRegion.Match(Parsed("<area><polygon>invalid</polygon></area>"), Wugu));
    }

    [Fact] public async Task Only_saved_location_filters_alerts_and_failed_area_lookup_is_not_reported_as_no_alerts()
    {
        using var handler = new Handler { Areas = Area("新北市新店區", "6500600") }; using var http = new HttpClient(handler);
        var settings = new Settings(); var clock = new Clock();
        using var weather = new WeatherService(settings, new(http), clock, new(http));
        await weather.SaveAsync(new(true, Wugu)); await weather.RefreshAsync();
        var vm = new WeatherViewModel(weather, settings, clock); await vm.LoadAsync();
        Assert.Empty(vm.AlertBadges); Assert.Empty(vm.AlertStatus); Assert.Contains("五股區", vm.AlertHeading);
        vm.SelectedCounty = "新北市"; vm.SelectedDistrict = vm.Districts.Single(d => d.District == "新店區"); vm.Tick();
        Assert.Empty(vm.AlertBadges); // Merely editing the form must not change the home location.
        await vm.SaveDraftAsync(); Assert.Single(vm.AlertBadges); Assert.Contains("新店區", vm.AlertHeading);
        Assert.Equal(1, handler.AlertCalls); // Area-rich snapshots can be filtered without another download.
        handler.FailAreas = true; await weather.RefreshAsync(true); vm.Tick();
        Assert.Empty(vm.AlertBadges); Assert.Contains("影響範圍待確認", vm.AlertStatus);
        Assert.False(weather.AlertsStale);
        handler.FailAreas = false; handler.Areas = Area("北部海面"); await weather.RefreshAsync(true); vm.Tick();
        Assert.Empty(vm.AlertBadges); Assert.Contains("影響範圍待確認", vm.AlertStatus);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = WeatherAlertTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Browser : IBrowserLauncher { public Uri? Opened; public void Open(Uri uri) => Opened = uri; }
    private sealed class Settings : ISettingsRepository, IAdminSettingsStore
    {
        private readonly Dictionary<string, Setting> entries = [];
        public Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(entries.GetValueOrDefault(key));
        public Task SaveAsync(Setting setting, CancellationToken cancellationToken = default) { entries[setting.Key] = setting; return Task.CompletedTask; }
        public async Task SaveAsync(IReadOnlyList<Setting> values, CancellationToken ct = default) { foreach (var value in values) await SaveAsync(value, ct); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string Alerts = Feed(Entry());
        public bool FailAlerts, HangAlerts, FailAreas;
        public string Areas = Area("新北市", "65");
        public int AlertCalls, ForecastCalls;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "alerts.ncdr.nat.gov.tw")
            {
                if (request.RequestUri.AbsolutePath.StartsWith("/Capstorage/", StringComparison.Ordinal))
                {
                    if (FailAreas) throw new HttpRequestException("CAP unavailable");
                    var id = System.IO.Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath);
                    var item = CwaAlertClient.Parse(Alerts, Now).Single(a => a.Id == id);
                    return new(HttpStatusCode.OK) { Content = new StringContent(Cap(id, Info(Areas, item.Title))) };
                }
                AlertCalls++; Started.TrySetResult();
                if (HangAlerts) await Task.Delay(Timeout.Infinite, cancellationToken);
                if (FailAlerts) throw new HttpRequestException("offline");
                return new(HttpStatusCode.OK) { Content = new StringContent(Alerts) };
            }
            ForecastCalls++;
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"current":{"time":"2026-09-30T10:00","temperature_2m":29,"apparent_temperature":31,"precipitation":0,"weather_code":2},
                "daily":{"time":["2026-09-30","2026-10-01"],"weather_code":[2,61],"temperature_2m_min":[25,24],"temperature_2m_max":[32,31],"precipitation_probability_max":[20,60]}}
                """) };
        }
    }
}
