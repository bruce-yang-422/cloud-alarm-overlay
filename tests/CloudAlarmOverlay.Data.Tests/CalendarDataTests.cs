using System.Net;
using System.Text.Json;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class CalendarDataTests:IDisposable,IAppPaths
{
    public string DataDirectory{get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmCalendarTests",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private readonly Clock clock=new();
    private readonly Source source=new();
    private readonly ServiceProvider services;
    public CalendarDataTests()
    {
        services=new ServiceCollection().AddCore().AddData().AddSingleton<IAppPaths>(this).AddSingleton<TimeProvider>(clock).AddSingleton<ICalendarDataSource>(source).BuildServiceProvider();
        Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    private CalendarDataService Calendar=>Get<CalendarDataService>();
    private async Task Personal()=>await Get<IDeviceIdentityService>().SetInitialIdentityAsync("CAL","測試",UsageModes.Personal);
    private async Task Enable(string frequency="Daily")
    {
        await Personal();await Calendar.InitializeAsync();await Calendar.SaveOptionsAsync(new(){Enabled=true,Frequency=frequency});
        var state=await Calendar.GetAsync();
        source.Holidays=JsonSerializer.Serialize(state.Holidays! with{Version="updated"},CalendarJson.Options);
        source.Lunar=JsonSerializer.Serialize(state.Lunar! with{Version="updated"},CalendarJson.Options);
    }
    [Fact] public async Task Packaged_json_matches_csv_and_seeds_without_network_or_changing_sheet_overrides()
    {
        await Get<IHolidayRepository>().ReplaceCacheAsync([new(){Date=new(2026,1,1),Type="補班日",Note="公司覆寫"}]);
        await Get<ILunarCalendarRepository>().ReplaceCacheAsync([new(){Date=new(2026,1,5),LunarDay=17,SolarTerm="公司節氣"}]);
        await Calendar.InitializeAsync();await Calendar.InitializeAsync();
        var state=await Calendar.GetAsync();Assert.False(state.Options.Enabled);Assert.Equal(0,source.Calls);
        Assert.Equal(730,state.Lunar!.Entries.Length);Assert.Equal(46,state.Holidays!.Entries.Length);
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Sheet範例"));
        var parser=Get<ICsvSheetParser>();
        Assert.Equal(parser.ParseHolidays(await File.ReadAllTextAsync(Path.Combine(root,"SheetA_Holidays.csv"))).Select(h=>(h.Date,h.Type,h.Note)),state.Holidays.Entries.Select(h=>(h.Date,h.Type,h.Note)));
        Assert.Equal(parser.ParseLunarCalendar(await File.ReadAllTextAsync(Path.Combine(root,"SheetA_LunarCalendar.csv"))).Select(l=>(l.Date,l.LunarDate,l.LunarDay,l.SolarTerm)),state.Lunar.Entries.Select(l=>(l.Date,l.LunarDate,l.LunarDay,l.SolarTerm)));
        Assert.Equal("補班日",(await Get<IHolidayRepository>().GetAllAsync()).Single(h=>h.Date==new DateOnly(2026,1,1)).Type);
        Assert.Equal("公司節氣",(await Get<ILunarCalendarRepository>().GetByDateAsync(new(2026,1,5)))!.SolarTerm);
        Assert.Equal(730,(await Get<ILunarCalendarRepository>().GetAllAsync()).Count);
        await Get<IHolidayRepository>().ReplaceCacheAsync([]);Assert.Equal("國定假日",(await Get<IHolidayRepository>().GetAllAsync()).Single(h=>h.Date==new DateOnly(2026,1,1)).Type);
    }
    [Fact] public async Task Disabled_and_manual_frequency_never_poll_and_admin_is_required()
    {
        await Calendar.UpdateAsync(false);Assert.Equal(0,source.Calls);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Calendar.SaveOptionsAsync(new(){Enabled=true}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Calendar.UpdateAsync(true));
        await Enable("Manual");await Calendar.UpdateAsync(false);Assert.Equal(0,source.Calls);
        var updated=await Calendar.UpdateAsync(true);Assert.Equal(2,source.Calls);Assert.Equal("GitHub",updated.Source);Assert.Equal("updated",updated.Lunar!.Version);
        await Calendar.UpdateAsync(false);Assert.Equal(2,source.Calls);
        await Calendar.SaveOptionsAsync(new(){Enabled=false});await Assert.ThrowsAsync<InvalidOperationException>(()=>Calendar.UpdateAsync(true));Assert.Equal(2,source.Calls);
    }
    [Fact] public async Task Daily_attempt_survives_restart_and_failure_preserves_both_old_datasets()
    {
        await Enable();var original=await Calendar.GetAsync();source.Fail=true;
        var failed=await Calendar.UpdateAsync(false);Assert.Equal(original.Lunar,failed.Lunar);Assert.Equal(original.Holidays,failed.Holidays);Assert.Null(failed.LastSuccessAt);
        Assert.Contains("保留原資料",failed.LastResult);Assert.Equal(2,source.Calls);
        var restarted=new CalendarDataService(Get<ICalendarDataStore>(),source,Get<AdminSession>(),clock,Get<ChangeSignal>());
        await restarted.UpdateAsync(false);Assert.Equal(2,source.Calls);
        clock.At=clock.At.AddDays(1);source.Fail=false;await restarted.UpdateAsync(false);Assert.Equal(4,source.Calls);
        var updated=await restarted.GetAsync();Assert.Equal("updated",updated.Lunar!.Version);Assert.NotNull(updated.LastSuccessAt);
        var again=new CalendarDataService(Get<ICalendarDataStore>(),source,Get<AdminSession>(),clock,Get<ChangeSignal>());await again.InitializeAsync();
        Assert.Equal("updated",(await again.GetAsync()).Lunar!.Version); // A packaged copy cannot replace a downloaded update.
    }
    [Theory]
    [InlineData("mismatch")]
    [InlineData("invalid")]
    public async Task Invalid_pair_never_partially_replaces_data(string scenario)
    {
        await Enable();var before=await Calendar.GetAsync();
        source.Lunar=scenario=="invalid"?"<html>404</html>":source.Lunar.Replace("updated","different");
        var result=await Calendar.UpdateAsync(true);
        Assert.Equal(before.Holidays,result.Holidays);Assert.Equal(before.Lunar,result.Lunar);Assert.Equal(before.AppliedAt,result.AppliedAt);Assert.Null(result.LastSuccessAt);
    }
    [Theory]
    [InlineData("Daily","2026-09-28T23:59:00+00:00","2026-09-29T00:00:00+00:00",true)]
    [InlineData("Weekly","2026-09-27T12:00:00+00:00","2026-09-28T00:00:00+00:00",true)]
    [InlineData("Weekly","2026-09-28T01:00:00+00:00","2026-10-04T23:59:00+00:00",false)]
    [InlineData("Monthly","2026-09-30T23:59:00+00:00","2026-10-01T00:00:00+00:00",true)]
    [InlineData("Monthly","2026-10-01T00:00:00+00:00","2026-10-31T23:59:00+00:00",false)]
    [InlineData("Manual","2026-01-01T00:00:00+00:00","2026-10-31T23:59:00+00:00",false)]
    public void Schedule_uses_calendar_periods(string frequency,string previous,string now,bool due)
        =>Assert.Equal(due,new CalendarUpdateOptions{Enabled=true,Frequency=frequency}.IsDue(DateTimeOffset.Parse(now),DateTimeOffset.Parse(previous)));
    [Fact] public void Urls_require_public_github_json_and_convert_blob_links()
    {
        Assert.Equal("https://raw.githubusercontent.com/owner/repo/main/data/test.json",CalendarUpdateOptions.NormalizeUrl("https://github.com/owner/repo/blob/main/data/test.json"));
        foreach(var url in new[]{"http://raw.githubusercontent.com/owner/repo/main/data.json","https://example.com/test.json","https://github.com/owner/repo","https://raw.githubusercontent.com/owner/repo/main/data.json?token=secret"})
            Assert.Throws<ArgumentException>(()=>CalendarUpdateOptions.NormalizeUrl(url));
    }
    [Fact] public async Task Schema_rejects_duplicate_dates_missing_days_and_bad_types()
    {
        await Calendar.InitializeAsync();var state=await Calendar.GetAsync();
        Assert.Throws<FormatException>(()=>CalendarJson.ReadLunar(JsonSerializer.Serialize(state.Lunar! with{Entries=state.Lunar!.Entries[..^1]},CalendarJson.Options)));
        Assert.Throws<FormatException>(()=>CalendarJson.ReadHolidays(JsonSerializer.Serialize(state.Holidays! with{Entries=[state.Holidays!.Entries[0],state.Holidays.Entries[0]]},CalendarJson.Options)));
        Assert.Throws<FormatException>(()=>CalendarJson.ReadHolidays(JsonSerializer.Serialize(state.Holidays! with{Entries=[state.Holidays!.Entries[0] with{Type="bad"}]},CalendarJson.Options)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<ISettingsRepository>().SaveAsync(new(){Key=CalendarDataState.Key,Value="{}"}));
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    private sealed class Clock:TimeProvider
    {
        public DateTimeOffset At=new(2026,9,29,10,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>At;
        public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.Utc;
    }
    private sealed class Source:ICalendarDataSource
    {
        public int Calls;public bool Fail;public string Holidays="",Lunar="";
        public Task<string> DownloadAsync(string url,CancellationToken ct=default)
        {
            Calls++;
            return Fail?Task.FromException<string>(new HttpRequestException("test",null,HttpStatusCode.NotFound)):Task.FromResult(url.EndsWith("SheetA_Holidays.json")?Holidays:Lunar);
        }
    }
}
