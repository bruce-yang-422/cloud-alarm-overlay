using System.Globalization;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Recurrence;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
namespace CloudAlarmOverlay.Data.Tests;

public sealed class MilestoneOneTests : IDisposable
{
    private readonly Paths paths = new();
    private readonly ServiceProvider services;
    public MilestoneOneTests()
    {
        var collection = new ServiceCollection().AddCore().AddData();
        collection.AddSingleton<IAppPaths>(paths);
        collection.AddSingleton<ISheetCsvClient, FakeCsv>();
        services = collection.BuildServiceProvider();
        services.GetRequiredService<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private T Get<T>() where T : notnull => services.GetRequiredService<T>();
    private static AlarmTask TaskAt(DateTime at, string id = "local") => new() { Id = id, Title = "月報表", ScheduledAt = at, CreatedAt = at.AddDays(-1), UpdatedAt = at.AddDays(-1) };
    [Fact]
    public async Task Local_CRUD_roundtrips_unicode_note_dates_and_retains_history_snapshot()
    {
        var task = TaskAt(DateTime.Now.AddMinutes(5)) with { Note = "內部備註", Description = "第一行\n第二行" };
        await Get<ITaskService>().SaveLocalAsync(task);
        var read = await Get<ITaskRepository>().GetByIdAsync(task.Id);
        Assert.Equal(task.Note, read!.Note); Assert.Equal(task.ScheduledAt, read.ScheduledAt); Assert.Equal(task.Description, read.Description);
        var runtime = Get<IRuntimeStore>(); var id = OccurrenceIdentity.For(task.Id, task.ScheduledAt);
        Assert.True(await runtime.ClaimAsync(id, task, task.ScheduledAt));
        Assert.False(await runtime.ClaimAsync("different-id", task, task.ScheduledAt));
        await runtime.DisplayedAsync(id, task, task.ScheduledAt, new Device { DeviceId = "IT-001" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Get<ITaskRepository>().DeleteLocalAsync(task.Id));
        await runtime.CompleteAsync(id);
        await Get<ITaskService>().SaveLocalAsync(task with { Title = "改名" });
        await Get<ITaskService>().DeleteLocalAsync(task.Id);
        Assert.Null(await Get<ITaskRepository>().GetByIdAsync(task.Id));
        var log = Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today, DateTime.Now.AddDays(1)));
        Assert.Equal("月報表", log.TaskName); Assert.Equal("Acknowledged", log.Result); Assert.NotNull(log.AcknowledgedAt);
        Assert.Null(log.SyncedAt); Assert.Equal("NotApplicable", log.SyncStatus);
    }
    [Fact]
    public async Task Cloud_refresh_is_atomic_and_never_overwrites_local_or_other_source()
    {
        var repo = Get<ITaskRepository>(); var local = TaskAt(DateTime.Now.AddHours(1));
        await repo.SaveLocalAsync(local);
        var cloud = local with { Id = "SheetA:1", ExternalId = "1", Source = TaskSources.SheetA };
        await repo.ReplaceCloudCacheAsync(TaskSources.SheetA, [cloud]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveLocalAsync(cloud));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteLocalAsync(cloud.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ReplaceCloudCacheAsync(TaskSources.SheetA, [cloud with { Title = "should rollback" }, cloud with { Id = local.Id }]));
        Assert.Equal(local.Title, (await repo.GetByIdAsync(cloud.Id))!.Title);
        await repo.ReplaceCloudCacheAsync(TaskSources.SheetA, []);
        Assert.Single(await repo.GetAllAsync()); Assert.NotNull(await repo.GetByIdAsync(local.Id));
    }
    [Fact]
    public async Task Overdue_confirmation_retains_overdue_result_and_recovery_preserves_unacked_history()
    {
        var runtime = Get<IRuntimeStore>(); var task = TaskAt(DateTime.Now.AddMinutes(-20));
        await Get<ITaskRepository>().SaveLocalAsync(task);
        await runtime.ClaimAsync("overdue", task, task.ScheduledAt);
        await runtime.DisplayedAsync("overdue", task, task.ScheduledAt, new Device { DeviceId = "IT" });
        await Get<Database>().ExecuteAsync("UPDATE AcknowledgementLogs SET TriggeredAt=@at;", new { at = DateTime.Now.AddMinutes(-16) });
        await runtime.MarkOverdueAsync();
        Assert.Equal("Overdue_Unacked", Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today, DateTime.Now)).Result);
        await runtime.CompleteAsync("overdue");
        Assert.Equal("Overdue_Acknowledged", Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today, DateTime.Now)).Result);
        await runtime.ClaimAsync("unshown", task, task.ScheduledAt.AddDays(1)); await runtime.RecoverAsync();
        Assert.False(await runtime.IsActiveAsync(task.Id));
    }
    [Fact]
    public async Task Trigger_log_surfaces_every_occurrence_including_ones_that_never_produced_an_ack_log_row()
    {
        var runtime = Get<IRuntimeStore>(); var task = TaskAt(DateTime.Now.AddDays(-2), "daily-task");
        await Get<ITaskRepository>().SaveLocalAsync(task);
        var device = new Device { DeviceId = "IT-001" };

        // Day 1: displayed and acknowledged on time.
        var day1 = task.ScheduledAt;
        await runtime.ClaimAsync("d1", task, day1);
        await runtime.DisplayedAsync("d1", task, day1, device);
        await runtime.CompleteAsync("d1");

        // Day 2: claimed by AlarmWorker but never actually displayed (e.g. crash before DisplayedAsync) —
        // this occurrence must still show up in the trigger log even though AcknowledgementLogs has nothing for it.
        var day2 = task.ScheduledAt.AddDays(1);
        await runtime.ClaimAsync("d2", task, day2);

        var log = await Get<IAckLogRepository>().GetTriggerLogAsync(task.Id, day1.AddDays(-1), day2.AddDays(1));
        Assert.Equal(2, log.Count);
        var acknowledged = Assert.Single(log, e => e.OccurrenceId == "d1");
        Assert.Equal("Acknowledged", acknowledged.OccurrenceState); Assert.Equal("Acknowledged", acknowledged.Result); Assert.NotNull(acknowledged.TriggeredAt);
        var failed = Assert.Single(log, e => e.OccurrenceId == "d2");
        Assert.Equal("Claimed", failed.OccurrenceState); Assert.Null(failed.Result); Assert.Null(failed.TriggeredAt);
    }
    [Theory]
    [InlineData("None", "2026-09-15T09:00:00")]
    [InlineData("Daily", "2026-09-15T09:00:00")]
    [InlineData("Weekly:1,3,5", "2026-09-16T09:00:00")]
    [InlineData("Monthly:31", "2026-10-31T09:00:00")]
    public async Task Next_occurrence_obeys_calendar_rules(string rule, string expected)
    {
        var task = TaskAt(new DateTime(2026, 9, 15, 9, 0, 0)) with { Recurrence = rule };
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task, new DateTime(2026, 9, 15)));
    }
    [Fact]
    public async Task Holidays_makeup_and_lunar_missing_data_have_conservative_behavior()
    {
        await Get<IHolidayRepository>().ReplaceCacheAsync([new Holiday { Date = new(2026, 9, 19), Type = "補班日" }, new Holiday { Date = new(2026, 9, 18), Type = "國定假日" }]);
        var task = TaskAt(new(2026, 9, 15, 9, 0, 0)) with { Recurrence = "Weekly:1,2,3,4,5", SkipOnHoliday = true };
        Assert.Equal(new DateTime(2026, 9, 19, 9, 0, 0), await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task, new(2026, 9, 18)));
        Assert.Null(await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task with { Recurrence = "LunarDay:1,15" }, new(2026, 9, 18)));
        await Get<ILunarCalendarRepository>().ReplaceCacheAsync([new LunarCalendarEntry { Date = new(2026, 9, 20), LunarDay = 15 }]);
        Assert.Equal(new DateTime(2026, 9, 20, 9, 0, 0), await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task with { Recurrence = "LunarDay:1,15" }, new(2026, 9, 18)));
        Assert.Null(await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task with { Recurrence = "None" }, new(2026, 9, 18)));
    }
    [Fact]
    public void Csv_uses_headers_skips_description_handles_quotes_BOM_and_defaults()
    {
        const string csv = "\uFEFFTitle,Enabled,Id,Time,Unknown\n名稱,啟用,編號,時間,未知\n\"會議,確認\",TRUE,1,2026-09-15 09:00,ignore\n";
        var task = Assert.Single(Get<ICsvSheetParser>().ParseTasks(csv, TaskSources.SheetA));
        Assert.Equal("會議,確認", task.Title); Assert.Equal("SheetA:1", task.Id); Assert.Equal("中級", task.Level); Assert.Equal("None", task.Recurrence);
        Assert.Throws<FormatException>(() => Get<ICsvSheetParser>().ParseTasks("Id,Title\n編號,名稱\n", TaskSources.SheetA));
        Assert.Throws<FormatException>(() => Get<ICsvSheetParser>().ParseTasks(csv + "會議,TRUE,1,2026-09-15 09:00,x\n", TaskSources.SheetA));
    }
    [Fact]
    public void All_repository_csv_examples_parse_without_credentials()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Samples");
        var parser = Get<ICsvSheetParser>();
        Assert.NotEmpty(parser.ParseTasks(File.ReadAllText(Path.Combine(root, "SheetA_Tasks.csv")), TaskSources.SheetA));
        // SheetB_Tasks.csv is now the official, intentionally-empty template teams fill in themselves
        // (header + Chinese description rows only) — assert it parses cleanly without throwing, not that it has rows.
        Assert.Empty(parser.ParseTasks(File.ReadAllText(Path.Combine(root, "SheetB_Tasks.csv")), TaskSources.SheetB));
        Assert.NotEmpty(parser.ParseEmployees(File.ReadAllText(Path.Combine(root, "SheetA_Employees.csv"))));
        Assert.NotEmpty(parser.ParseHolidays(File.ReadAllText(Path.Combine(root, "SheetA_Holidays.csv"))));
        Assert.NotEmpty(parser.ParseLunarCalendar(File.ReadAllText(Path.Combine(root, "SheetA_LunarCalendar.csv"))));
    }
    [Fact]
    public void Audience_exclusion_wins_and_department_failure_falls_back_to_direct_keys()
    {
        var device = new Device { DeviceId = "IT-001", DisplayName = "小明" };
        Employee[] employees = [new() { DeviceId = "IT-001", Name = "小明", Department = "資訊部" }];
        var task = TaskAt(DateTime.Now) with { TargetDeviceOrName = "資訊部" };
        var filter = Get<IAudienceFilterService>();
        Assert.True(filter.IsIncluded(task, device, employees)); Assert.False(filter.IsIncluded(task, device, []));
        Assert.True(filter.IsIncluded(task with { TargetDeviceOrName = "IT-001" }, device, []));
        Assert.False(filter.IsIncluded(task with { ExcludeDeviceOrName = "小明" }, device, employees));
    }
    [Fact]
    public void Reading_codes_exclude_ambiguous_characters_and_use_four_three_format()
    {
        for (int i = 0; i < 500; i++) Assert.Matches("^[0-9]{3}-[0-9]{3}-[0-9]{3}$", Get<IAckCodeGenerator>().Generate());
    }
    [Fact]
    public async Task Identity_is_explicit_required_and_cannot_be_replaced()
    {
        var identity = Get<IDeviceIdentityService>();
        Assert.Null(await identity.GetLocalAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => identity.SetInitialIdentityAsync("", "小明"));
        await identity.SetInitialIdentityAsync(" IT-001 ", " 小明 ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => identity.SetInitialIdentityAsync("other", "other"));
        Assert.Equal("IT-001", (await identity.GetLocalAsync())!.DeviceId);
    }
    [Fact]
    public async Task Sync_isolates_failures_filters_before_storage_and_keeps_offline_cache()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("IT-001", "小明");
        await Get<IAuthenticationService>().CreateInitialAsync("admin","test-password");
        Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("admin","test-password"));
        await Get<SyncConfiguration>().SaveAsync(new() { SheetAId = "A", TasksAGid = "1", EmployeesGid = "2", HolidaysGid = "3", LunarGid = "4", SheetBId = "B", TasksBGid = "5" });
        var client = (FakeCsv)Get<ISheetCsvClient>();
        client.Csv["A/2"] = "Id,Name,Department\n編號,名稱,部門\nIT-001,小明,資訊部";
        client.Csv["A/3"] = "Date,Type\n日期,類型";
        client.Csv["A/4"] = "Date,LunarDay\n日期,農曆";
        client.Csv["A/1"] = "Id,Time,Title,Enabled,TargetDeviceOrName\n編號,時間,名稱,啟用,對象\n1,2026-09-15 09:00,自己的任務,TRUE,資訊部\n2,2026-09-15 09:00,其他人的任務,TRUE,other";
        client.Csv["B/5"] = "broken";
        await Get<ISyncService>().SyncAsync();
        var task = Assert.Single(await Get<ITaskRepository>().GetAllAsync()); Assert.Equal("自己的任務", task.Title);
        Assert.Contains(await Get<ISyncLogRepository>().GetRangeAsync(DateTime.Today, DateTime.Now), l => l.Source == "SheetB/Tasks" && l.Status == "失敗");
        client.Csv.Clear();
        await Get<ISyncService>().SyncAsync();
        Assert.Equal(task.Id, Assert.Single(await Get<ITaskRepository>().GetAllAsync()).Id);
        // Employees is unavailable but Tasks succeeds: no stale department expansion.
        client.Csv["A/1"]="Id,Time,Title,Enabled,TargetDeviceOrName\n編號,時間,名稱,啟用,對象\n1,2026-09-15 09:00,部門任務,TRUE,資訊部\n3,2026-09-15 09:00,個人任務,TRUE,小明";
        await Get<ISyncService>().SyncAsync();
        Assert.Equal("個人任務",Assert.Single(await Get<ITaskRepository>().GetAllAsync()).Title);
        Assert.Single(await Get<IEmployeeRepository>().GetAllAsync()); // last successful notification ceiling cache is retained.
    }
    [Fact]
    public async Task V1_upgrades_transactionally_with_existing_task_and_log_snapshot()
    {
        var other = new Paths();
        try
        {
            var factory = new SqliteConnectionFactory(other);
            await using (var c = await factory.OpenConnectionAsync())
            {
                using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V1.sql")!;
                using var reader = new StreamReader(stream);
                using var command = c.CreateCommand();
                command.CommandText = await reader.ReadToEndAsync() + """
                    INSERT INTO Tasks(Id,Title,ScheduledAt,Source,Level,CreatedAt,UpdatedAt)
                    VALUES('old','舊任務','2026-09-15T09:00:00','本機','中級','2026-09-14','2026-09-14');
                    INSERT INTO AcknowledgementLogs(Id,TaskId,DeviceId,TriggeredAt,Result)
                    VALUES('ack','old','IT','2026-09-15T09:00:00','Acknowledged');
                    PRAGMA user_version=1;
                    """;
                await command.ExecuteNonQueryAsync();
            }
            await new DatabaseInitializer(factory).InitializeAsync();
            var db = new Database(factory);
            Assert.Equal(4, Assert.Single(await db.QueryAsync<int>("PRAGMA user_version;")));
            Assert.Equal("舊任務", Assert.Single(await db.QueryAsync<string>("SELECT TaskName FROM AcknowledgementLogs;")));
            Assert.Equal("本機", Assert.Single(await db.QueryAsync<string>("SELECT Source FROM AcknowledgementLogs;")));
        }
        finally { Directory.Delete(other.DataDirectory, true); }
    }
    public void Dispose() { services.Dispose(); Directory.Delete(paths.DataDirectory, true); }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "CloudAlarmOverlay.M1.Tests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
    }
    private sealed class FakeCsv : ISheetCsvClient
    {
        public Dictionary<string, string> Csv { get; } = [];
        public Task<string> DownloadAsync(string id, string gid, CancellationToken ct = default) => Csv.TryGetValue(id + "/" + gid, out var csv) ? Task.FromResult(csv) : Task.FromException<string>(new HttpRequestException("離線"));
    }
}
