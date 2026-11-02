using System.Globalization;
using System.Text;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class CalendarIcsExportTests:IDisposable,IAppPaths
{
    public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CalendarIcsTests",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private readonly ServiceProvider services;
    private readonly CalendarStore calendar=new();
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    public CalendarIcsExportTests()
    {
        var collection=new ServiceCollection();collection.AddCore();collection.AddData();collection.AddSingleton<IAppPaths>(this);collection.AddSingleton<ICalendarDataStore>(calendar);
        services=collection.BuildServiceProvider();Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private static AlarmTask TaskAt(string id,DateTime at)=>new(){Id=id,Title=id,ScheduledAt=at,CreatedAt=at.AddDays(-1),UpdatedAt=at};
    private static CalendarExportOptions Range(int fromYear=2026,int toYear=2026)=>new(new(fromYear,1,1),new(toYear,12,31)){TimeZoneId="Asia/Taipei"};
    private static string Unfold(string content)=>content.Replace("\r\n ","");
    private static string[] Values(string content,string key)=>Unfold(content).Split("\r\n").Where(line=>line.StartsWith(key+":",StringComparison.Ordinal)).Select(line=>line[(key.Length+1)..]).ToArray();
    [Fact] public async Task Exports_only_local_tasks_escapes_text_folds_utf8_and_uses_utc()
    {
        var task=TaskAt("one",new(2026,11,2,9,30,0)) with{Title="中文測試😀,;\\"+new string('農',55),Description="第一行\r\nEND:VEVENT\n第二行",Note="備註"};
        var repo=Get<ITaskRepository>();await repo.SaveLocalAsync(task);
        await repo.ReplaceCloudCacheAsync(TaskSources.SheetA,[task with{Id="cloud",ExternalId="cloud",Source=TaskSources.SheetA}]);
        var result=await Get<ICalendarIcsExporter>().ExportAsync(Range());
        Assert.Equal(1,result.TaskEvents);Assert.Equal(0,result.CountdownEvents);
        Assert.Equal("20261102T013000Z",Assert.Single(Values(result.Content,"DTSTART")));
        Assert.Equal("20261102T020000Z",Assert.Single(Values(result.Content,"DTEND")));
        Assert.Contains("\\,\\;\\\\",Unfold(result.Content));Assert.Contains("第一行\\nEND:VEVENT\\n第二行",Unfold(result.Content));
        Assert.Equal(1,Values(result.Content,"BEGIN").Count(value=>value=="VEVENT"));
        Assert.All(result.Content.Split("\r\n"),line=>Assert.True(Encoding.UTF8.GetByteCount(line)<=75));
        Assert.DoesNotContain("\n",result.Content.Replace("\r\n",""));
        Assert.EndsWith("END:VCALENDAR\r\n",result.Content);
    }
    [Fact] public async Task Expands_ten_years_of_lunar_new_year_without_relying_on_two_year_cache()
    {
        await Get<ITaskRepository>().SaveLocalAsync(TaskAt("lunar",new(2026,1,1,9,30,0)) with{Recurrence="LunarDate:1:1"});
        var result=await Get<ICalendarIcsExporter>().ExportAsync(Range(2026,2035));
        Assert.Equal(10,result.EventCount);Assert.Empty(result.Warnings);Assert.DoesNotContain("RRULE",result.Content);
        Assert.Contains("20260217T013000Z",Values(result.Content,"DTSTART"));Assert.Contains("20270206T013000Z",Values(result.Content,"DTSTART"));
        Assert.Contains(Values(result.Content,"DTSTART"),date=>date.StartsWith("2035",StringComparison.Ordinal));
    }
    [Fact] public async Task Lunar_leap_month_option_preserves_both_occurrences()
    {
        await Get<ITaskRepository>().SaveLocalAsync(TaskAt("leap",new(2025,1,1,12,0,0)) with{Recurrence="LunarDate:6:1:Both"});
        var result=await Get<ICalendarIcsExporter>().ExportAsync(Range(2025,2025));Assert.Equal(2,result.EventCount);
    }
    [Fact] public async Task Weekday_export_observes_holidays_and_makeup_workdays()
    {
        var task=TaskAt("work",new(2026,11,2,9,0,0)) with{Recurrence="Weekly:1,2,3,4,5",SkipOnHoliday=true};
        await Get<ITaskRepository>().SaveLocalAsync(task);
        await Get<IHolidayRepository>().ReplaceCacheAsync([new(){Date=new(2026,11,3),Type="國定假日"},new(){Date=new(2026,11,7),Type="補班日"}]);
        var result=await Get<ICalendarIcsExporter>().ExportAsync(new(new(2026,11,2),new(2026,11,8)){TimeZoneId="Asia/Taipei"});
        Assert.Equal(5,result.EventCount);Assert.Empty(result.Warnings);
        Assert.DoesNotContain("20261103T010000Z",Values(result.Content,"DTSTART"));Assert.Contains("20261107T010000Z",Values(result.Content,"DTSTART"));
    }
    [Fact] public async Task Long_holiday_ranges_warn_instead_of_silently_claiming_complete_future_data()
    {
        await Get<ITaskRepository>().SaveLocalAsync(TaskAt("annual",new(2026,11,5,10,0,0)) with{Recurrence="Monthly:5:11",SkipOnHoliday=true});
        var result=await Get<ICalendarIcsExporter>().ExportAsync(Range(2026,2045));
        Assert.Equal(20,result.EventCount);Assert.Contains("2027/12/31",Assert.Single(result.Warnings));
        Assert.Contains("未來公告變更",result.Warnings[0]);
    }
    [Fact] public async Task Date_only_countdowns_and_multi_day_activities_keep_exclusive_end_dates_and_reminders()
    {
        var task=TaskAt("trip",new(2026,11,1,18,0,0)) with{ActivityStartAt=new(2026,11,2),ActivityEndAt=new(2026,11,5),ActivityAllDay=true};
        await Get<ITaskRepository>().SaveLocalAsync(task);
        await Get<ICountdownRepository>().SaveAsync(new(){Id="launch",Title="上線",TargetAt=new(2026,11,5),CreatedAt=new(2026,1,1),ReminderDays=1,ReminderMinutes=540});
        var result=await Get<ICalendarIcsExporter>().ExportAsync(new(new(2026,11,2),new(2026,11,5)){TimeZoneId="Asia/Taipei"});
        Assert.Equal(2,result.EventCount);Assert.Contains("DTSTART;VALUE=DATE:20261102",result.Content);Assert.Contains("DTEND;VALUE=DATE:20261105",result.Content);
        Assert.Contains("DTSTART;VALUE=DATE:20261105",result.Content);Assert.Contains("DTEND;VALUE=DATE:20261106",result.Content);
        Assert.Contains("TRIGGER;VALUE=DATE-TIME:20261104T010000Z",result.Content);
        Assert.Contains("TRIGGER;VALUE=DATE-TIME:20261101T100000Z",result.Content);
    }
    [Fact] public async Task Disabled_and_completed_items_are_opt_in_and_never_receive_reminders()
    {
        var task=TaskAt("daily",new(2026,11,2,9,0,0)) with{Recurrence="Daily"};
        await Get<ITaskRepository>().SaveLocalAsync(task);await Get<ITaskCompletionRepository>().CompleteAsync(task,task.ScheduledAt);
        await Get<ITaskRepository>().SaveLocalAsync(task with{Id="disabled",Recurrence="None",Enabled=false});
        await Get<ICountdownRepository>().SaveAsync(new(){Id="done",Title="完成事件",TargetAt=new(2026,11,2),CreatedAt=new(2026,1,1),CompletedAt=new(2026,11,2),Repeat="Yearly",ReminderDays=0});
        var range=new CalendarExportOptions(new(2026,11,2),new(2026,11,3)){TimeZoneId="Asia/Taipei"};
        Assert.Equal(1,(await Get<ICalendarIcsExporter>().ExportAsync(range)).EventCount);
        var all=await Get<ICalendarIcsExporter>().ExportAsync(range with{IncludeCompleted=true,IncludeDisabledTasks=true});
        Assert.Equal(4,all.EventCount);Assert.Equal(1,Values(all.Content,"BEGIN").Count(value=>value=="VALARM"));
        Assert.DoesNotContain("VALARM",(await Get<ICalendarIcsExporter>().ExportAsync(range with{IncludeReminders=false})).Content);
    }
    [Fact] public async Task Uids_are_stable_across_ranges_and_distinguish_task_and_countdown_ids()
    {
        await Get<ITaskRepository>().SaveLocalAsync(TaskAt("same",new(2026,11,2,9,0,0)));
        await Get<ICountdownRepository>().SaveAsync(new(){Id="same",Title="同編號",TargetAt=new(2026,11,2),CreatedAt=new(2026,1,1)});
        var first=await Get<ICalendarIcsExporter>().ExportAsync(Range());
        var second=await Get<ICalendarIcsExporter>().ExportAsync(Range(2026,2029));
        Assert.Equal(2,Values(first.Content,"UID").Distinct().Count());Assert.Equal(Values(first.Content,"UID"),Values(second.Content,"UID"));
    }
    [Fact] public async Task Gregorian_month_ends_are_skipped_and_custom_range_can_exceed_ten_years()
    {
        await Get<ITaskRepository>().SaveLocalAsync(TaskAt("month-end",new(2026,1,31,12,0,0)) with{Recurrence="Monthly:31"});
        var result=await Get<ICalendarIcsExporter>().ExportAsync(Range());Assert.Equal(7,result.EventCount);
        Assert.DoesNotContain(Values(result.Content,"DTSTART"),value=>value.StartsWith("202602",StringComparison.Ordinal));
    }
    [Fact] public async Task Invalid_ranges_and_unsupported_lunar_years_fail_without_partial_output()
    {
        var service=Get<ICalendarIcsExporter>();
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ExportAsync(new(new(2026,2,1),new(2026,1,1))));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ExportAsync(Range() with{IncludeTasks=false,IncludeCountdowns=false}));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ExportAsync(Range(2026,2126)));
        await Get<ITaskRepository>().SaveLocalAsync(TaskAt("lunar",new(2026,1,1)) with{Recurrence="LunarDay:1,15"});
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ExportAsync(Range(2099,2105)));
    }
    [Fact] public async Task Cancelled_exports_stop_before_output()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Get<ICalendarIcsExporter>().ExportAsync(Range(),cancellation.Token));
    }
    [Fact] public async Task Too_many_expanded_events_are_rejected_instead_of_truncating_the_file()
    {
        var task=TaskAt("daily-a",new(2026,1,1,9,0,0)) with{Recurrence="Daily"};
        await Get<ITaskRepository>().SaveLocalAsync(task);await Get<ITaskRepository>().SaveLocalAsync(task with{Id="daily-b"});
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<ICalendarIcsExporter>().ExportAsync(Range(2026,2125)));
        Assert.Contains("50,000",error.Message);
    }
    [Fact] public async Task Nonexistent_daylight_saving_time_is_reported_instead_of_shifted_silently()
    {
        await Get<ITaskRepository>().SaveLocalAsync(TaskAt("gap",new(2026,3,8,2,30,0)));
        var error=await Assert.ThrowsAsync<ArgumentException>(()=>Get<ICalendarIcsExporter>().ExportAsync(Range() with{TimeZoneId="America/New_York"}));
        Assert.Contains("不存在",error.Message);
    }
    private sealed class CalendarStore:ICalendarDataStore
    {
        public CalendarDataState State=new(){Holidays=new(1,"Holidays","test",new(2026,1,1),new(2027,12,31),[])};
        public Task<CalendarDataState> ReadAsync(CancellationToken ct=default)=>Task.FromResult(State);
        public Task SaveAsync(CalendarDataState state,CancellationToken ct=default){State=state;return Task.CompletedTask;}
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
}
