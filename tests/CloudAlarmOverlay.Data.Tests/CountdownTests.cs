using System.IO.Compression;
using System.Text.Json;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class CountdownTests : IDisposable
{
    private readonly Paths paths = new();
    private readonly ServiceProvider services;
    private T Get<T>() where T : notnull => services.GetRequiredService<T>();
    public CountdownTests()
    {
        var collection = new ServiceCollection();
        collection.AddCore(); collection.AddData(); collection.AddSingleton<IAppPaths>(paths);
        services = collection.BuildServiceProvider();
        Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }

    [Theory]
    [InlineData("Days", "2028-03-01", "2028-02-28T23:59:59", "還有 2 天")]
    [InlineData("Days", "2028-03-01", "2028-02-29T00:00:00", "還有 1 天")]
    [InlineData("Days", "2028-03-01", "2028-03-01T23:59:59", "就是今天")]
    [InlineData("Days", "2028-03-01", "2028-03-02", "已到期")]
    [InlineData("Time", "2026-09-19T10:00", "2026-09-18T08:59:59", "1 天 1 時 1 分")]
    [InlineData("Time", "2026-09-19T10:00", "2026-09-19T09:00", "1 時 0 分")]
    [InlineData("Time", "2026-09-19T10:00", "2026-09-19T09:59:59", "1 分鐘")]
    [InlineData("Time", "2026-09-19T10:00", "2026-09-19T10:00", "已到期")]
    [InlineData("Time", "2026-09-19T10:00", "2026-10-01", "已到期")]
    public void Countdown_uses_current_clock_calendar_days_and_never_negative(string mode, string target, string now, string expected)
        => Assert.Equal(expected, new CountdownItem { Mode = mode, TargetAt = DateTime.Parse(target) }.Remaining(DateTime.Parse(now)));

    [Fact]
    public async Task Save_edit_pin_reopen_and_delete_do_not_create_tasks_or_occurrences()
    {
        var repo = Get<ICountdownRepository>();
        var item = Sample();
        await repo.SaveAsync(item);
        await repo.SaveAsync(item with { Title = "更新的目標", IsPinned = false, Mode = "Time", TargetAt = item.TargetAt.AddHours(9) });
        await Get<IDatabaseInitializer>().InitializeAsync();
        var reopened = new ServiceCollection().AddCore().AddData().AddSingleton<IAppPaths>(paths).BuildServiceProvider();
        using (reopened)
        {
            var loaded = Assert.Single(await reopened.GetRequiredService<ICountdownRepository>().GetAllAsync());
            Assert.Equal("更新的目標", loaded.Title); Assert.False(loaded.IsPinned);
            Assert.Equal(item.TargetAt.AddHours(9), loaded.TargetAt); Assert.Equal(item.CreatedAt, loaded.CreatedAt);
            await reopened.GetRequiredService<ICountdownRepository>().DeleteAsync(loaded.Id);
        }
        Assert.Empty(await repo.GetAllAsync());
        Assert.Empty(await Get<Database>().QueryAsync<AlarmTask>("SELECT * FROM Tasks;"));
        Assert.Empty(await Get<Database>().QueryAsync<string>("SELECT Id FROM Occurrences;"));
        foreach (var invalid in new[] { item with { Title = " " }, item with { Title = new string('a',81) }, item with { Mode = "Unknown" }, item with { TargetAt = default }, item with { TargetAt = item.TargetAt.AddTicks(1) }, item with { TargetAt = item.TargetAt.AddHours(1) } })
            await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAsync(invalid));
        Assert.Empty(await repo.GetAllAsync());
    }

    [Fact]
    public async Task Backup_roundtrip_restores_pins_and_keeps_existing_items_on_conflict_and_legacy_restore()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST", "倒數測試");
        var repo = Get<ICountdownRepository>();
        var item = Sample() with { Category = "旅行", Direction = "Up", DisplayFormat = "YearsMonthsDays", Recurrence="LunarDate:3:23:Both", SkipOnHoliday=true, CompletedAt = new DateTime(2026,9,20,15,0,0) }; await repo.SaveAsync(item);
        var backups = Get<IBackupRestoreService>(); var path = Path.Combine(paths.DataDirectory, "countdowns.calbak");
        await backups.CreateBackupAsync(path);
        Assert.Equal(1, (await backups.InspectAsync(path)).Countdowns);
        await repo.DeleteAsync(item.Id);
        Assert.Equal(1, (await backups.RestoreAsync(path, false, null)).ImportedCountdowns);
        Assert.Equal(item, Assert.Single(await repo.GetAllAsync()));
        await repo.SaveAsync(item with { Title = "本機較新", IsPinned = false });
        Assert.Equal(1, (await backups.RestoreAsync(path, false, null)).SkippedCountdowns);
        var current = Assert.Single(await repo.GetAllAsync());
        Assert.Equal("本機較新", current.Title); Assert.False(current.IsPinned);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("countdowns.json")!.Delete(); zip.GetEntry("manifest.json")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write("{\"Format\":\"CloudAlarmOverlay\",\"Version\":1}");
        }
        Assert.Equal(0, (await backups.InspectAsync(path)).Countdowns);
        await backups.RestoreAsync(path);
        Assert.Equal(current, Assert.Single(await repo.GetAllAsync()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    public async Task Invalid_new_backup_is_rejected_before_any_data_changes(string error)
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST", "原身分");
        var repo = Get<ICountdownRepository>(); await repo.SaveAsync(Sample());
        var backups = Get<IBackupRestoreService>(); var path = Path.Combine(paths.DataDirectory, "bad.calbak");
        await backups.CreateBackupAsync(path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("countdowns.json")!.Delete();
            if (error != "missing")
            {
                using var writer = new StreamWriter(zip.CreateEntry("countdowns.json").Open());
                writer.Write(JsonSerializer.Serialize(error == "duplicate" ? new[] { Sample(), Sample() } : new[] { Sample() with { Mode = "invalid" } }));
            }
        }
        await Get<Database>().ExecuteAsync("UPDATE Devices SET DisplayName='保留';");
        await Assert.ThrowsAnyAsync<Exception>(() => backups.RestoreAsync(path));
        Assert.Equal("保留", (await Get<IDeviceRepository>().GetLocalAsync())!.DisplayName);
        Assert.Equal(Sample(), Assert.Single(await repo.GetAllAsync()));
    }

    [Fact]
    public async Task Home_pin_limit_is_atomic_and_restore_keeps_all_items_without_exceeding_two()
    {
        var repo=Get<ICountdownRepository>();
        var attempts=await Task.WhenAll(Enumerable.Range(0,3).Select(i=>Task.Run(async()=>
        {
            try { await repo.SaveAsync(Sample() with { Id="pin-"+i }); return true; }
            catch(InvalidOperationException ex) { Assert.Contains("最多釘選 2",ex.Message); return false; }
        })));
        Assert.Equal(2,attempts.Count(success=>success));
        var originals=await repo.GetAllAsync();
        await repo.SaveAsync(originals[0] with { Title="編輯既有釘選",IsTop=true });
        Assert.True((await repo.GetAllAsync()).Single(i=>i.Id==originals[0].Id).IsTop);
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","測試");
        var backups=Get<IBackupRestoreService>();var path=Path.Combine(paths.DataDirectory,"pins.calbak");
        await backups.CreateBackupAsync(path);
        foreach(var item in originals)await repo.DeleteAsync(item.Id);
        await repo.SaveAsync(Sample() with { Id="current" });
        await backups.RestoreAsync(path);
        var restored=await repo.GetAllAsync();
        Assert.Equal(3,restored.Count);Assert.Equal(2,restored.Count(i=>i.IsPinned));
        Assert.True(restored.Single(i=>i.Id=="current").IsPinned);
        Assert.True(restored.Single(i=>i.Id==originals[0].Id).IsTop);
        await repo.SaveAsync(restored.Single(i=>i.Id=="current") with { IsPinned=false });
        var unpinned=restored.Single(i=>!i.IsPinned);
        await repo.SaveAsync(unpinned with { IsPinned=true });
        Assert.Equal(2,(await repo.GetAllAsync()).Count(i=>i.IsPinned));
    }

    [Theory]
    [InlineData("Weekly", "2026-09-18", "2026-09-19", "2026-09-25")]
    [InlineData("Monthly", "2026-01-31", "2026-02-01", "2026-03-31")]
    [InlineData("Monthly", "2026-01-31", "2026-04-01", "2026-05-31")]
    [InlineData("Yearly", "2024-02-29", "2097-01-01", "2104-02-29")]
    public void Repeats_preserve_original_day_and_skip_nonexistent_dates(string repeat, string target, string from, string expected)
    {
        var item = new CountdownItem { TargetAt = DateTime.Parse(target), Repeat = repeat };
        Assert.Equal(DateTime.Parse(expected), item.NextTarget(DateTime.Parse(from)));
        Assert.Equal(DateTime.Parse(expected), item.DisplayTarget(DateTime.Parse(from)));
    }

    [Fact]
    public void Custom_reminders_cross_month_year_boundaries_and_do_not_backfill_configuration_changes()
    {
        var item = new CountdownItem { Title="周年", TargetAt=new(2027,1,1), CreatedAt=new(2026,9,18), Repeat="Yearly", ReminderDays=3, ReminderMinutes=14*60+35 };
        var first = new DateTime(2026,12,29,14,35,0);
        Assert.Equal(first, item.NextReminder(new(2026,12,1)));
        Assert.Equal(new DateTime(2027,12,29,14,35,0), item.NextReminder(first));
        Assert.Null((item with { Repeat="None" }).NextReminder(first));
        Assert.Null((item with { ReminderDays=-1 }).NextReminder(first.AddDays(-2)));
        Assert.Null((item with { CompletedAt=first.AddDays(-1) }).NextReminder(first.AddDays(-2)));
        Assert.Equal(new DateTime(2027,12,29,14,35,0), (item with { ReminderChangedAt=first.AddMinutes(1) }).NextReminder(first.AddDays(-1)));
        var task=item.ReminderTask(first);
        Assert.Equal(AlarmLevels.Low,task.Level); Assert.Contains("2027/01/01",task.Description);
        var leap = item with { TargetAt=new(2024,2,29), ReminderDays=1, CreatedAt=new(2024,1,1), ReminderMinutes=8*60+10 };
        Assert.Equal(new DateTime(2028,2,28,8,10,0),leap.NextReminder(new(2025,1,1)));
    }

    [Fact]
    public async Task Reminder_claims_are_deduplicated_and_can_record_history_without_a_task_row()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST", "倒數測試");
        var item=Sample();var at=new DateTime(2026,9,24,14,35,0);var task=item.ReminderTask(at);
        var runtime=Get<IRuntimeStore>();var id=OccurrenceIdentity.For(task.Id,at);
        Assert.True(await runtime.ClaimAsync(id,task,at)); Assert.False(await runtime.ClaimAsync(id,task,at));
        await runtime.DisplayedAsync(id,task,at,(await Get<IDeviceIdentityService>().GetLocalAsync())!);
        await runtime.CompleteAsync(id);
        var log=Assert.Single(await Get<Database>().QueryAsync<AcknowledgementLog>("SELECT * FROM AcknowledgementLogs;"));
        Assert.Equal(task.Title,log.TaskName); Assert.Contains("備註測試",JsonSerializer.Deserialize<AlarmTask>(log.TaskSnapshotJson!)!.Description);
        Assert.Empty(await Get<ITaskRepository>().GetAllAsync());
    }

    [Theory]
    [InlineData("Days", "2026-09-01", "2026-09-18", "17 天")]
    [InlineData("WeeksDays", "2026-09-01", "2026-09-18", "2 週 3 天")]
    [InlineData("MonthsDays", "2026-01-31", "2026-03-01", "1 個月 1 天")]
    [InlineData("MonthsDays", "2026-01-31", "2026-03-30", "1 個月 30 天")]
    [InlineData("YearsMonthsDays", "2024-02-29", "2025-02-28", "1 年 0 個月 0 天")]
    [InlineData("YearsMonthsDays", "2008-04-16", "2026-09-18", "18 年 5 個月 2 天")]
    [InlineData("YearsMonthsDays", "2026-09-18", "2026-09-18", "0 年 0 個月 0 天")]
    public void Calendar_display_formats_use_real_months(string format, string start, string end, string expected)
    {
        var from = DateTime.Parse(start); var to = DateTime.Parse(end);
        Assert.Equal(expected, CountdownItem.FormatInterval(from, to, format));
        var up = Sample() with { TargetAt = from, Direction = "Up", DisplayFormat = format };
        Assert.Equal("已過 " + expected, up.Remaining(to));
    }

    [Fact]
    public void Count_up_keeps_original_start_with_repeating_reminders_and_handles_future_and_time()
    {
        var up = Sample() with { Direction="Up", Mode="Time", TargetAt=new(2025,9,18,9,0,0), Repeat="Yearly", ReminderDays=0, ReminderMinutes=540, CreatedAt=new(2025,1,1), ReminderChangedAt=new(2025,1,1) };
        Assert.Equal("尚未開始",up.Remaining(new(2025,9,18,8,59,59)));
        Assert.Equal("已過 0 天 00 時 00 分",up.Remaining(up.TargetAt));
        Assert.Equal("已過 365 天 04 時 30 分",up.Remaining(new(2026,9,18,13,30,0)));
        Assert.Equal(up.TargetAt,up.DisplayTarget(new(2026,9,19)));
        Assert.Equal(new DateTime(2027,9,18,9,0,0),up.NextReminder(new(2026,9,19)));
        Assert.Throws<ArgumentException>(() => (up with { Direction="bad" }).Validate());
        Assert.Throws<ArgumentException>(() => (up with { DisplayFormat="bad" }).Validate());
    }

    [Theory]
    [InlineData("Daily")]
    [InlineData("Weekly:1,3,5")]
    [InlineData("Weekly:1,2,3,4,5")]
    [InlineData("Monthly:31:1,3,5,7,9,11")]
    [InlineData("LunarDay:1,15")]
    [InlineData("LunarDate:3:23:Regular")]
    public async Task Countdown_and_task_share_occurrences_holidays_and_lunar_data(string rule)
    {
        var lunar = new[]{new LunarCalendarEntry { Date=new(2026,9,20),LunarDay=15 },new LunarCalendarEntry { Date=new(2026,10,2),LunarDay=1 }};
        var holidays=new[]{new Holiday { Date=new(2026,9,19),Type="補班日" },new Holiday { Date=new(2026,9,21),Type="國定假日" }};
        await Get<ILunarCalendarRepository>().ReplaceCacheAsync(lunar); await Get<IHolidayRepository>().ReplaceCacheAsync(holidays);
        var map=lunar.ToDictionary(e=>e.Date,e=>e.LunarDay);
        var item=Sample() with { Recurrence=rule,Mode="Time",TargetAt=new(2026,9,1,9,0,0),CreatedAt=new(2026,9,1),ReminderChangedAt=new(2026,9,1),ReminderDays=0,ReminderMinutes=540,SkipOnHoliday=true };
        var task=new AlarmTask { Id="comparison",CreatedAt=item.CreatedAt,UpdatedAt=item.CreatedAt,Title="對照",ScheduledAt=item.TargetAt,Recurrence=rule,SkipOnHoliday=true };
        var after=new DateTime(2026,9,18,10,0,0);
        for(var i=0;i<3;i++)
        {
            var expected=await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task,after);
            Assert.Equal(expected,item.NextReminder(after,map,holidays));
            if(expected is not {} at)break;
            after=at;
        }
        if(rule=="LunarDay:1,15") Assert.Null(item.NextReminder(after));
    }

    [Fact]
    public void Specific_lunar_date_observes_leap_month_policy_and_advance_reminders()
    {
        var item=Sample() with { TargetAt=new(2023,1,1),CreatedAt=new(2023,1,1),ReminderChangedAt=new(2023,1,1),Recurrence="LunarDate:2:1:Regular",ReminderDays=3,ReminderMinutes=540 };
        Assert.Equal(new DateTime(2023,2,20),item.NextTarget(new(2023,1,1)));
        Assert.NotEqual(new DateTime(2023,3,22),item.NextTarget(new(2023,2,21)));
        var both=item with { Recurrence="LunarDate:2:1:Both" };
        Assert.Equal(new DateTime(2023,3,22),both.NextTarget(new(2023,2,21)));
        Assert.Equal(new DateTime(2023,3,19,9,0,0),both.NextReminder(new(2023,2,21)));
        Assert.Equal(new DateTime(2024,2,10),(item with { Recurrence="LunarDate:1:1:Regular" }).NextTarget(new(2024,1,1)));
        foreach(var invalid in new[]{"LunarDate:0:1","LunarDate:13:1","LunarDate:1:31","LunarDate:1:1:Unknown"})
            Assert.Throws<FormatException>(() => (item with { Recurrence=invalid }).Validate());
    }

    [Fact]
    public void Advance_reminder_also_respects_holiday_on_notification_date()
    {
        var item=Sample() with { Recurrence="Daily",TargetAt=new(2026,9,20),CreatedAt=new(2026,9,1),ReminderChangedAt=new(2026,9,1),ReminderDays=1,ReminderMinutes=540,SkipOnHoliday=true };
        Holiday[] holidays=[new() { Date=new(2026,9,19),Type="國定假日" }];
        Assert.Equal(new DateTime(2026,9,20,9,0,0),item.NextReminder(new(2026,9,18),null,holidays));
    }

    private static CountdownItem Sample() => new() { Id = "holiday", Title = "期待放假 🌙", TargetAt = new(2026,9,25), CreatedAt = new(2026,9,18,10,0,0), IsPinned = true, IsTop=true,
        Category="節日", Repeat="Yearly", ReminderDays=1, ReminderMinutes=875, Notes="備註測試", ReminderChangedAt=new(2026,9,18,10,0,0) };
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "CloudAlarmCountdownTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory,"test.db");
    }
    public void Dispose() { services.Dispose(); if (Directory.Exists(paths.DataDirectory)) Directory.Delete(paths.DataDirectory,true); }
}
