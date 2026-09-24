using System.IO.Compression;
using System.Globalization;
using System.Text;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CsvHelper;
using Microsoft.Extensions.DependencyInjection;
namespace CloudAlarmOverlay.Data.Tests;

public sealed class PlannedFeaturesTests : IDisposable, IAppPaths
{
    public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmPlannedFeatures",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private readonly ServiceProvider services;
    private readonly Clock clock=new();
    private sealed class Clock:TimeProvider
    {
        public DateTime Now=DateTime.Now;
        public override DateTimeOffset GetUtcNow()=>new DateTimeOffset(Now).ToUniversalTime();
    }
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    public PlannedFeaturesTests()
    {
        var collection=new ServiceCollection();collection.AddCore();collection.AddData();collection.AddSingleton<IAppPaths>(this);collection.AddSingleton<TimeProvider>(clock);
        services=collection.BuildServiceProvider();Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private static AlarmTask Item(string id="test")=>new(){Id=id,Title="會議",ScheduledAt=DateTime.Today.AddDays(1).AddHours(9),CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
    private async Task<(AlarmTask Task,string Id)> Display(string recurrence="None")
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","測試");
        var task=Item() with {Recurrence=recurrence};await Get<ITaskService>().SaveLocalAsync(task);
        var id=OccurrenceIdentity.For(task.Id,task.ScheduledAt);
        await Get<IRuntimeStore>().ClaimAsync(id,task,task.ScheduledAt);
        await Get<IRuntimeStore>().DisplayedAsync(id,task,task.ScheduledAt,(await Get<IDeviceIdentityService>().GetLocalAsync())!);
        return (task,id);
    }
    [Fact] public async Task Snooze_preserves_occurrence_snapshot_schedule_count_and_final_acknowledgement()
    {
        var (task,id)=await Display();var runtime=Get<IRuntimeStore>();var snooze=Get<ISnoozeStore>();
        var before=Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today,DateTime.Today.AddDays(2)));
        for(var count=1;count<=3;count++)
        {
            var until=DateTime.Now.AddMinutes(5);await snooze.DeferAsync(id,until);
            Assert.Equal(count,await snooze.CountAsync(id));Assert.True(await runtime.IsActiveAsync(task.Id));
            Assert.Equal(until,Assert.Single(await Get<Database>().QueryAsync<DateTime>("SELECT SnoozedUntil FROM Occurrences WHERE Id=@id;",new{id})));
            await runtime.DisplayedAsync(id,task,task.ScheduledAt,(await Get<IDeviceIdentityService>().GetLocalAsync())!);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(()=>snooze.DeferAsync(id,DateTime.Now.AddMinutes(5)));
        await runtime.CompleteAsync(id);
        var after=Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today,DateTime.Today.AddDays(2)));
        Assert.Equal(3,after.SnoozeCount);Assert.Equal(before.TriggeredAt,after.TriggeredAt);Assert.NotNull(after.AcknowledgedAt);
        Assert.Equal(before.TaskSnapshotJson,after.TaskSnapshotJson);Assert.Equal(task.ScheduledAt,(await Get<ITaskRepository>().GetByIdAsync(task.Id))!.ScheduledAt);
        Assert.Equal(3,Assert.Single(await Get<IAckLogRepository>().GetTriggerLogAsync(task.Id,DateTime.Today,DateTime.Today.AddDays(2))).SnoozeCount);
        Assert.False(await runtime.IsActiveAsync(task.Id));
    }
    [Fact] public async Task Restart_marks_even_future_snoozes_missed_without_reclaiming()
    {
        var (task,id)=await Display();await Get<ISnoozeStore>().DeferAsync(id,DateTime.Now.AddMinutes(30));
        await Get<IRuntimeStore>().RecoverAsync();
        Assert.False(await Get<IRuntimeStore>().ClaimAsync(id,task,task.ScheduledAt));
        Assert.False(await Get<IRuntimeStore>().IsActiveAsync(task.Id));
        var log=Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today,DateTime.Today.AddDays(2)));
        Assert.Equal("Overdue_Unacked",log.Result);Assert.Equal(1,log.SnoozeCount);Assert.Null(log.AcknowledgedAt);
    }
    [Fact] public async Task Urgent_snooze_policy_is_admin_only()
    {
        Assert.True(await Get<NotificationPreferences>().AllowUrgentSnoozeAsync());
        var setting=new Setting{Key="AllowUrgentSnooze",Value="false",Locked=true};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<ISettingsRepository>().SaveAsync(setting with {Locked=false}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<IAdminSettingsStore>().SaveAsync([setting]));
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("admin","12345"));await Get<IAdminSettingsStore>().SaveAsync([setting]);
        Assert.False(await Get<NotificationPreferences>().AllowUrgentSnoozeAsync());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Backup_roundtrips_counts_and_accepts_older_csv_without_new_column(bool legacy)
    {
        var (_,id)=await Display();await Get<ISnoozeStore>().DeferAsync(id,DateTime.Now.AddMinutes(5));
        await Get<IRuntimeStore>().RecoverAsync();
        var file=Path.Combine(DataDirectory,"test.calbak");await Get<IBackupRestoreService>().CreateBackupAsync(file);
        if(legacy)
        {
            using var zip=ZipFile.Open(file,ZipArchiveMode.Update);var entry=zip.GetEntry("acklog.csv")!;
            string text;using(var reader=new StreamReader(entry.Open()))text=await reader.ReadToEndAsync();
            var rows=new List<string[]>();using(var reader=new CsvReader(new StringReader(text),CultureInfo.InvariantCulture))
            { while(reader.Read())rows.Add(reader.Parser.Record!.ToArray()); }
            var index=Array.IndexOf(rows[0],"SnoozeCount");Assert.True(index>=0);
            using var output=new StringWriter();using(var writer=new CsvWriter(output,CultureInfo.InvariantCulture))
            {foreach(var row in rows){foreach(var field in row.Where((_,i)=>i!=index))writer.WriteField(field);writer.NextRecord();}}
            entry.Delete();using var stream=new StreamWriter(zip.CreateEntry("acklog.csv").Open());await stream.WriteAsync(output.ToString());
        }
        await Get<Database>().ExecuteAsync("DELETE FROM AcknowledgementLogs; DELETE FROM Occurrences;");
        await Get<IBackupRestoreService>().RestoreAsync(file);
        var log=Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today,DateTime.Today.AddDays(2)));
        Assert.Equal(legacy?0:1,log.SnoozeCount);Assert.Equal("Overdue_Unacked",log.Result);
    }
    [Fact] public async Task Version_nine_upgrade_keeps_tasks_and_old_logs()
    {
        var (task,id)=await Display();await Get<IRuntimeStore>().CompleteAsync(id);
        await Get<Database>().ExecuteAsync("ALTER TABLE AcknowledgementLogs DROP COLUMN SnoozeCount; ALTER TABLE Occurrences DROP COLUMN SnoozedUntil; PRAGMA user_version=9;");
        await Get<IDatabaseInitializer>().InitializeAsync();
        Assert.Equal(task.Title,(await Get<ITaskRepository>().GetByIdAsync(task.Id))!.Title);
        Assert.Equal(0,await Get<ISnoozeStore>().CountAsync(id));
    }
    [Fact] public async Task Csv_preview_keeps_valid_rows_when_others_fail_and_ignores_audience()
    {
        var service=Get<LocalTaskCsvService>();
        var csv="Id,Time,Title,Enabled,Level,Recurrence,TargetDeviceOrName,ExcludeDeviceOrName\n編號,時間,名稱,啟用,等級,重複,對象,排除\n"+
            $"valid,{DateTime.Today.AddDays(1):yyyy-MM-dd} 09:00,工作,TRUE,重要提醒,None,other-device,TEST\n"+
            "bad,invalid,錯誤,TRUE,重要提醒,None,,\nold,2020-01-01 09:00,過期,TRUE,重要提醒,None,,\nrepeat,2020-01-01 09:00,重複,TRUE,一般提醒,Daily,,\nmax,2099-01-01 09:00,強制,TRUE,強制通知,None,,\n";
        var rows=await service.PreviewAsync(csv);Assert.Equal(5,rows.Count);Assert.Equal(2,rows.Count(r=>r.Valid));
        Assert.Null(rows[0].Task!.TargetDeviceOrName);Assert.Null(rows[0].Task!.ExcludeDeviceOrName);Assert.True(rows[3].NextReminder>DateTime.Now);
        var result=await service.ImportAsync(rows,false);Assert.Equal(new TaskImportResult(2,3),result);
        Assert.All(await Get<ITaskRepository>().GetAllAsync(),t=>Assert.Equal(TaskSources.Local,t.Source));
    }
    [Fact] public async Task Csv_roundtrip_preserves_quotes_multiline_notes_and_recurrence()
    {
        var task=Item() with {Title="含逗號,與引號\"",Description="第一行\n第二行,內容",Note="備註,\"文字\"\n下一行",Recurrence="Monthly:10:1,3,5",Enabled=false};
        var rows=await Get<LocalTaskCsvService>().PreviewAsync("\uFEFF"+LocalTaskCsvService.Export([task]));
        var parsed=Assert.Single(rows).Task!;Assert.Equal(task.Title,parsed.Title);Assert.Equal(task.Description,parsed.Description);Assert.Equal(task.Note,parsed.Note);
        Assert.Equal(task.Recurrence,parsed.Recurrence);Assert.False(parsed.Enabled);
    }
    [Fact] public async Task Csv_duplicate_policy_never_overwrites_local_or_cloud_tasks_even_after_preview()
    {
        var service=Get<LocalTaskCsvService>();var task=Item();
        var rows=await service.PreviewAsync(LocalTaskCsvService.Export([task,task]));Assert.False(rows[0].Duplicate);Assert.True(rows[1].Duplicate);
        await Get<ITaskService>().SaveLocalAsync(task with{Title="保留"});
        Assert.Equal(new TaskImportResult(0,2),await service.ImportAsync(rows,false));
        Assert.Equal(new TaskImportResult(2,0),await service.ImportAsync(rows,true));
        Assert.Equal("保留",(await Get<ITaskRepository>().GetByIdAsync(task.Id))!.Title);
        var cloud=task with{Id="SheetA:one",ExternalId="one",Source=TaskSources.SheetA};
        await Get<ITaskRepository>().ReplaceCloudCacheAsync(TaskSources.SheetA,[cloud]);
        rows=await service.PreviewAsync(LocalTaskCsvService.Export([task with{Id=cloud.Id}]));
        Assert.True(rows[0].Duplicate);await service.ImportAsync(rows,true);
        Assert.Equal(TaskSources.SheetA,(await Get<ITaskRepository>().GetByIdAsync(cloud.Id))!.Source);
    }
    [Fact] public async Task Csv_template_has_two_valid_examples_and_invalid_utf8_has_actionable_error()
    {
        Assert.Equal(2,(await Get<LocalTaskCsvService>().PreviewAsync(LocalTaskCsvService.Template(DateTime.Now))).Count(r=>r.Valid));
        Assert.Contains("UTF-8",Assert.Throws<FormatException>(()=>LocalTaskCsvService.Decode([0xa4,0x40])).Message);
        Assert.Equal("中文",LocalTaskCsvService.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("中文")).ToArray()));
    }
    [Fact] public async Task Snooze_due_time_reopens_same_occurrence_and_leaves_next_recurring_schedule_unchanged()
    {
        var (task,id)=await Display("Daily");clock.Now=task.ScheduledAt;
        var next=await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task,task.ScheduledAt);
        var ticket=await Get<SnoozeService>().DeferAsync(id,task,task.ScheduledAt,10);Assert.NotNull(ticket);
        clock.Now=ticket.Until;Assert.True(await Get<SnoozeService>().WaitAsync(ticket));
        Assert.Equal(next,await Get<ITaskSchedulingService>().GetNextOccurrenceAsync((await Get<ITaskRepository>().GetByIdAsync(task.Id))!,task.ScheduledAt));
        Assert.Equal(id,ticket.OccurrenceId);Assert.Equal(task.Level,ticket.Task.Level);
    }
    [Fact] public async Task Snooze_overlapping_next_occurrence_is_merged_without_creating_another_notification()
    {
        var (task,id)=await Display("Daily");clock.Now=task.ScheduledAt.AddDays(1).AddMinutes(-5);
        Assert.Null(await Get<SnoozeService>().DeferAsync(id,task,task.ScheduledAt,5));
        Assert.Equal(1,await Get<ISnoozeStore>().CountAsync(id));Assert.False(await Get<IRuntimeStore>().IsActiveAsync(task.Id));
        Assert.True(await Get<IRuntimeStore>().ClaimAsync(OccurrenceIdentity.For(task.Id,task.ScheduledAt.AddDays(1)),task,task.ScheduledAt.AddDays(1)));
    }
    [Fact] public async Task Snooze_that_becomes_overdue_while_queued_is_not_displayed()
    {
        var (task,id)=await Display("Daily");clock.Now=task.ScheduledAt;
        var ticket=await Get<SnoozeService>().DeferAsync(id,task,task.ScheduledAt,5);Assert.NotNull(ticket);
        clock.Now=task.ScheduledAt.AddDays(1);
        Assert.False(await Get<SnoozeService>().ValidateAsync(ticket));
    }
    [Fact] public async Task Resume_after_deferred_deadline_marks_it_missed()
    {
        var (task,id)=await Display();clock.Now=DateTime.Now.AddHours(-1);
        var ticket=await Get<SnoozeService>().DeferAsync(id,task,task.ScheduledAt,5);Assert.NotNull(ticket);
        clock.Now=DateTime.Now;Get<RuntimeState>().Resume();
        Assert.False(await Get<SnoozeService>().WaitAsync(ticket));
        Assert.False(await Get<IRuntimeStore>().IsActiveAsync(task.Id));
    }
    [Fact] public async Task Snooze_rejects_unsupported_intervals()
    {
        var (task,id)=await Display();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<SnoozeService>().DeferAsync(id,task,task.ScheduledAt,1));
        Assert.Equal(0,await Get<ISnoozeStore>().CountAsync(id));
    }
    [Fact] public async Task Forced_cloud_notification_snoozes_without_acknowledgement_and_keeps_three_attempt_limit()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","測試");
        var task=Item("SheetA:forced") with{Source=TaskSources.SheetA,ExternalId="forced",Level=AlarmLevels.Max,RequireAcknowledgement=true};
        await Get<ITaskRepository>().ReplaceCloudCacheAsync(TaskSources.SheetA,[task]);
        var id=OccurrenceIdentity.For(task.Id,task.ScheduledAt);
        var device=(await Get<IDeviceIdentityService>().GetLocalAsync())!;
        var runtime=Get<IRuntimeStore>();await runtime.ClaimAsync(id,task,task.ScheduledAt);await runtime.DisplayedAsync(id,task,task.ScheduledAt,device);
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("admin","12345"));
        await Get<IAdminSettingsStore>().SaveAsync([new Setting{Key="AllowUrgentSnooze",Value="false",Locked=true}]);
        for(var count=1;count<=3;count++)
        {
            var ticket=await Get<SnoozeService>().DeferAsync(id,task,task.ScheduledAt,5);Assert.NotNull(ticket);
            Assert.Equal(AlarmLevels.Max,ticket.Task.Level);Assert.True(ticket.Task.RequireAcknowledgement);
            var log=Assert.Single(await Get<IAckLogRepository>().GetRangeAsync(DateTime.Today,DateTime.Today.AddDays(2)));
            Assert.Equal("Snoozed",log.Result);Assert.Null(log.AcknowledgedAt);Assert.Equal(count,log.SnoozeCount);
            clock.Now=ticket.Until;Assert.True(await Get<SnoozeService>().WaitAsync(ticket));
            await runtime.DisplayedAsync(id,task,task.ScheduledAt,device);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<SnoozeService>().DeferAsync(id,task,task.ScheduledAt,5));
    }
    [Fact] public async Task Csv_missing_description_row_is_rejected_instead_of_silently_dropping_first_task()
    {
        await Assert.ThrowsAsync<FormatException>(()=>Get<LocalTaskCsvService>().PreviewAsync("Id,Time,Title,Enabled\nfirst,2099-01-01 09:00,任務,TRUE"));
        var rows=await Get<LocalTaskCsvService>().PreviewAsync("Id,Time,Title,Enabled\n編號,時間,名稱,啟用\nbad,2099-01-01 09:00,任務\nvalid,2099-01-01 09:00,任務,TRUE");
        Assert.False(rows[0].Valid);Assert.True(rows[1].Valid);
    }
    [Fact] public async Task Countdown_share_branding_preference_is_validated_and_backed_up()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","測試");
        var settings=Get<ISettingsRepository>();var key=CountdownShareSnapshot.BrandingSettingKey;
        await Assert.ThrowsAsync<ArgumentException>(()=>settings.SaveAsync(new Setting{Key=key,Value="invalid"}));
        await settings.SaveAsync(new Setting{Key=key,Value="false"});
        var file=Path.Combine(DataDirectory,"share.calbak");await Get<IBackupRestoreService>().CreateBackupAsync(file);
        await settings.SaveAsync(new Setting{Key=key,Value="true"});await Get<IBackupRestoreService>().RestoreAsync(file);
        Assert.Equal("false",(await settings.GetAsync(key))?.Value);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"FormatVersion\":2,\"AllowUrgentSnooze\":false}")]
    [InlineData("{\"FormatVersion\":1,\"Password\":\"secret\"}")]
    [InlineData("{\"FormatVersion\":1,\"AllowUrgentSnooze\":null}")]
    [InlineData("{\"FormatVersion\":1,\"AllowUrgentSnooze\":true,\"AllowUrgentSnooze\":false}")]
    [InlineData("{\"FormatVersion\":1,\"FlashMilliseconds\":1}")]
    [InlineData("{\"FormatVersion\":1,\"LockQuiet\":true}")]
    [InlineData("{\"FormatVersion\":1,\"SyncOptions\":{\"SheetAId\":\"new\"}}")]
    [InlineData("{\"FormatVersion\":1,\"WeatherDefaultDistrictCode\":\"invalid\"}")]
    [InlineData("{\"FormatVersion\":1,\"QuietPeriods\":[null]}")]
    public void Admin_json_rejects_invalid_or_ambiguous_settings(string json)
        =>Assert.Throws<ArgumentException>(()=>AdminSettingsJson.Parse(json));

    [Fact] public async Task Admin_json_requires_login_is_atomic_and_redacts_connection_audit()
    {
        var imported=AdminSettingsJson.Parse(AdminSettingsJson.Template);
        var store=Get<IAdminSettingsStore>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveAsync(imported));
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();
        Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("admin","12345"));
        await store.SaveAsync(imported);
        var settings=Get<ISettingsRepository>();
        Assert.Equal("false",(await settings.GetAsync("SyncLinksLocked"))!.Value);
        Assert.Contains("板橋",System.Text.Json.JsonSerializer.Deserialize<WeatherLocation>((await settings.GetAsync("WeatherDefaultLocation"))!.Value!)!.Name);
        var before=(await settings.GetAsync("SyncOptions"))!.Value;
        var audit=await Get<Database>().QueryAsync<string>("SELECT NewValue FROM AuditLogs WHERE Action='SyncOptions'");
        Assert.Equal("已設定",Assert.Single(audit));
        await store.SaveAsync([new(){Key="SyncLinksLocked",Value="true"}]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([
            new(){Key="AllowUrgentSnooze",Value="false"},
            ..AdminSettingsJson.Parse(AdminSettingsJson.Template.Replace("REPLACE_WITH_SHEET_A_ID","changed"))]));
        Assert.Equal("true",(await settings.GetAsync("AllowUrgentSnooze"))!.Value);
        Assert.Equal(before,(await settings.GetAsync("SyncOptions"))!.Value);
        Assert.Equal("true",(await settings.GetAsync("SyncLinksLocked"))!.Value);
        await store.SaveAsync(AdminSettingsJson.Parse("{\"FormatVersion\":1,\"UpdateManifestUrl\":\"https://example.com/version.json\"}"));
        Assert.Equal(before,(await settings.GetAsync("SyncOptions"))!.Value);
        Assert.Equal("https://example.com/version.json",(await settings.GetAsync("UpdateManifestUrl"))!.Value);
        Get<AdminSession>().SignOut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveAsync(imported));
    }
    [Fact] public async Task Admin_json_export_roundtrips_policies_quiet_hours_and_legacy_weather_without_credentials()
    {
        var settings=Get<ISettingsRepository>();
        var defaults=AdminSettingsJson.Parse(await AdminSettingsJson.ExportAsync(settings));
        Assert.Equal("500",defaults.Single(s=>s.Key=="FlashMilliseconds").Value);
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();
        Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("admin","12345"));
        var imported=AdminSettingsJson.Parse(AdminSettingsJson.Template);
        await Get<IAdminSettingsStore>().SaveAsync(imported);
        await Get<IAdminSettingsStore>().SaveAsync([
            new(){Key="QuietPeriods",Value="[{\"Start\":\"22:00\",\"End\":\"07:00\"}]",Locked=true},
            new(){Key="FlashMilliseconds",Value="800",Locked=true},
            new(){Key="AllowUrgentSnooze",Value="false"},
            new(){Key="SyncLinksLocked",Value="true"}]);
        var json=await AdminSettingsJson.ExportAsync(settings);
        Assert.Contains("WeatherDefaultDistrictCode",json);
        Assert.DoesNotContain("StartTime",json);Assert.DoesNotContain("Password",json);Assert.DoesNotContain("12345",json);
        var roundtrip=AdminSettingsJson.Parse(json);
        Assert.Equal("true",roundtrip.Single(s=>s.Key=="SyncLinksLocked").Value);
        Assert.Equal("false",roundtrip.Single(s=>s.Key=="AllowUrgentSnooze").Value);
        Assert.True(roundtrip.Single(s=>s.Key=="QuietPeriods").Locked);
        Assert.Equal(await settings.GetAsync("FlashMilliseconds"),roundtrip.Single(s=>s.Key=="FlashMilliseconds"));
        var legacy=new WeatherLocation("自訂舊地點",25.01,121.01);
        await Get<IAdminSettingsStore>().SaveAsync([new(){Key="WeatherDefaultLocation",Value=System.Text.Json.JsonSerializer.Serialize(legacy)}]);
        var legacyExport=AdminSettingsJson.Parse(await AdminSettingsJson.ExportAsync(settings));
        Assert.Equal(legacy,System.Text.Json.JsonSerializer.Deserialize<WeatherLocation>(legacyExport.Single(s=>s.Key=="WeatherDefaultLocation").Value!));
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
}
