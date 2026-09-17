using System.Text.Json;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Recurrence;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class Version110Tests : IDisposable
{
    private readonly Paths paths = new();
    private readonly ServiceProvider services;
    public Version110Tests()
    {
        services = new ServiceCollection().AddCore().AddData().AddSingleton<IAppPaths>(paths).BuildServiceProvider();
        Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private T Get<T>() where T : notnull => services.GetRequiredService<T>();
    private static AlarmTask Sample(string rule) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Title = "月份提醒", Recurrence = rule,
        ScheduledAt = new(2024, 1, 10, 9, 30, 0), CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
    };

    [Theory]
    [InlineData("Monthly:10:1,3,5,7,9,11", "2026-10-01", "2026-11-10 09:30")]
    [InlineData("Monthly:10:1,4,7,10", "2026-10-11", "2027-01-10 09:30")]
    [InlineData("Monthly:31:2,4,7", "2026-01-01", "2026-07-31 09:30")]
    [InlineData("Monthly:10", "2026-10-10 09:30", "2026-11-10 09:30")]
    public async Task Month_selection_persists_and_schedules_across_years_and_short_months(string rule, string after, string expected)
    {
        var task = Sample(rule);
        await Get<ITaskService>().SaveLocalAsync(task);
        var saved = (await Get<ITaskRepository>().GetByIdAsync(task.Id))!;
        Assert.Equal(rule, saved.Recurrence);
        Assert.Equal(DateTime.Parse(expected), await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(saved, DateTime.Parse(after)));
    }

    [Theory]
    [InlineData("Monthly:10:")]
    [InlineData("Monthly:10:0")]
    [InlineData("Monthly:10:13")]
    [InlineData("Monthly:32:1")]
    public void Invalid_month_rules_are_rejected(string rule) => Assert.Throws<FormatException>(() => RecurrenceRule.Validate(rule));

    [Theory]
    [InlineData("Daily")]
    [InlineData("Weekly:1,2,3,4,5")]
    [InlineData("Weekly:2,4")]
    [InlineData("Monthly:10")]
    [InlineData("LunarDay:1,15")]
    public async Task Recurring_tasks_accept_past_start_and_find_future_occurrence(string rule)
    {
        var now = DateTime.Now;
        await Get<ILunarCalendarRepository>().ReplaceCacheAsync([new() { Date = DateOnly.FromDateTime(now.AddDays(1)), LunarDay = 1 }]);
        var task = Sample(rule);
        await Get<ITaskService>().SaveLocalAsync(task);
        Assert.True(await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task, now) > now);
        await Assert.ThrowsAsync<ArgumentException>(() => Get<ITaskService>().SaveLocalAsync(Sample("None")));
    }

    [Theory]
    [InlineData(TaskSources.SheetA)]
    [InlineData(TaskSources.SheetB)]
    public void Csv_accepts_month_selection_and_builder_workday_format(string source)
    {
        const string csv = "Id,Time,Title,Enabled,Recurrence\n編號,時間,標題,啟用,重複\nx,2026-01-01 09:30,月份,TRUE,\"Monthly:10:1,3,5\"";
        Assert.Equal("Monthly:10:1,3,5", Assert.Single(Get<ICsvSheetParser>().ParseTasks(csv, source)).Recurrence);
        Assert.Equal("Weekly:1,2,3,4,5", RecurrenceRule.NormalizeCsv("WORKDAY", DateTime.Now));
        Assert.Equal("Weekly:1,7", RecurrenceRule.NormalizeCsv("WEEKLY:1,7", DateTime.Now));
    }

    [Fact]
    public async Task History_keeps_original_markdown_after_task_edit_delete_and_backup_restore()
    {
        var task = Sample("Daily") with { Description = "## 原始內容\n- [ ] 確認", Note = "備註 ✅" };
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST-1", "測試");
        var device = (await Get<IDeviceIdentityService>().GetLocalAsync())!;
        await Get<ITaskService>().SaveLocalAsync(task);
        await Get<IRuntimeStore>().ClaimAsync("occurrence", task, DateTime.Now);
        await Get<IRuntimeStore>().DisplayedAsync("occurrence", task, DateTime.Now, device);
        await Get<IRuntimeStore>().CompleteAsync("occurrence");
        await Get<ITaskService>().SaveLocalAsync(task with { Description = "新內容" });
        await Get<ITaskService>().DeleteLocalAsync(task.Id);
        var entry = Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today, DateTime.Now.AddDays(1)));
        Assert.Equal(task.Description, JsonSerializer.Deserialize<AlarmTask>(entry.TaskSnapshotJson!)!.Description);
        var backup = Path.Combine(paths.DataDirectory, "snapshot.calbak");
        await Get<IBackupRestoreService>().CreateBackupAsync(backup);
        await Get<IBackupRestoreService>().RestoreAsync(backup);
        Assert.Equal(entry.TaskSnapshotJson, Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today, DateTime.Now.AddDays(1))).TaskSnapshotJson);
    }

    [Fact]
    public async Task Sync_logs_deduplicate_success_aggregate_failures_and_keep_recovery_configuration_changes()
    {
        var logs = Get<ISyncLogRepository>();
        var time = new DateTime(2026, 9, 17, 9, 0, 0);
        var entry = new SyncLogEntry { Source = "SheetB/Tasks", Time = time, Status = "成功", Message = "已下載 1 筆", RecordCount = 1 };
        await logs.RecordAsync(entry, "data1", "config1");
        for (var i = 1; i <= 10; i++) await logs.RecordAsync(entry with { Time = time.AddMinutes(i) }, "data1", "config1");
        Assert.Single(await logs.GetRangeAsync(time.AddDays(-1), time.AddDays(2)));
        Assert.Equal(time.AddMinutes(10), Assert.Single(await logs.GetStatesAsync()).LastCheckedAt);
        // Same count, changed content must still create a change event.
        await logs.RecordAsync(entry with { Time = time.AddMinutes(11) }, "data2", "config1");
        var failure = entry with { Time = time.AddMinutes(12), Status = "失敗", Message = "HTTP 403" };
        await logs.RecordAsync(failure, null, "config1");
        await logs.RecordAsync(failure with { Time = time.AddDays(1) }, null, "config1");
        var aggregate = Assert.Single(await logs.GetRangeAsync(time.AddDays(1).Date, time.AddDays(2)));
        Assert.Equal(2, aggregate.RepeatCount);
        Assert.Equal(time.AddDays(1), aggregate.LastSeenAt);
        await logs.RecordAsync(entry with { Time = time.AddDays(1).AddMinutes(1) }, "data2", "config1");
        await logs.RecordAsync(entry with { Time = time.AddDays(1).AddMinutes(2) }, "data2", "config2");
        var events = await logs.GetRangeAsync(time.AddDays(-1), time.AddDays(2));
        Assert.Equal(5, events.Count);
        Assert.Contains(events, e => e.EventKind == "Recovery");
        Assert.Contains(events, e => e.EventKind == "Configuration");
        Assert.Contains(events, e => e.EventKind == "Changed");
        await logs.AppendAsync(entry with { Time = time.AddYears(-1) });
        await logs.AppendAsync(failure with { Time = time.AddYears(-1) });
        Assert.Equal(1, await logs.PruneLegacySuccessAsync(time));
        Assert.Equal(6, (await logs.GetRangeAsync(time.AddYears(-2), time.AddDays(2))).Count);
    }

    [Fact]
    public async Task Heartbeat_does_not_wake_worker_and_reports_stall_error_and_recovery()
    {
        using var signal = new ChangeSignal();
        var clock = new Clock();
        var heartbeat = new AlarmHeartbeat(signal, clock);
        Assert.Equal("Pending", heartbeat.GetStatus(clock.Now.DateTime).Severity);
        heartbeat.Tick();
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.Equal("Healthy", heartbeat.GetStatus(clock.Now.DateTime).Severity);
        Assert.Equal("Stopped", heartbeat.GetStatus(clock.Now.DateTime.AddSeconds(121)).Severity);
        heartbeat.Fail("資料庫讀取失敗");
        Assert.Equal("Error", heartbeat.GetStatus(clock.Now.DateTime).Severity);
        heartbeat.Tick(); Assert.Equal("Healthy", heartbeat.GetStatus(clock.Now.DateTime).Severity);
        heartbeat.Stop(); Assert.Equal("Stopped", heartbeat.GetStatus(clock.Now.DateTime).Severity);
    }

    [Fact]
    public void Sync_health_distinguishes_missing_configuration_stale_data_failure_and_independent_sources()
    {
        var now = DateTime.Now;
        var device = new Device { DeviceId = "TEST-1" };
        var options = new SyncOptions { SheetBId = "B", TasksBGid = "5", IntervalSeconds = 45 };
        var state = new SyncState { Source = "SheetB/Tasks", ConfigFingerprint = SyncFingerprint.Configuration("SheetB/Tasks", options, device), Status = "成功", LastCheckedAt = now, LastSuccessAt = now };
        Assert.Equal("未設定", SyncHealth.ForSheet("SheetA", options, device, [state], now).Label);
        Assert.Equal("Healthy", SyncHealth.ForSheet("SheetB", options, device, [state], now).Severity);
        Assert.Equal("Stopped", SyncHealth.ForSheet("SheetB", options, device, [state], now.AddSeconds(136)).Severity);
        Assert.Equal("Error", SyncHealth.ForSheet("SheetB", options, device, [state with { Status = "失敗" }], now).Severity);
        Assert.Equal("Pending", SyncHealth.ForSheet("SheetB", options with { SheetBId = "other" }, device, [state], now).Severity);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.Now;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "CloudAlarmV110", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
    }
    public void Dispose() { services.Dispose(); Directory.Delete(paths.DataDirectory, true); }
}
