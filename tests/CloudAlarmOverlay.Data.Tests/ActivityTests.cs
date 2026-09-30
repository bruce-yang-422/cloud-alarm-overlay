using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
namespace CloudAlarmOverlay.Data.Tests;

public sealed class ActivityTests:IDisposable,IAppPaths
{
    public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"ActivityTests",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private readonly ServiceProvider services;
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    public ActivityTests(){var s=new ServiceCollection();s.AddCore();s.AddData();s.AddSingleton<IAppPaths>(this);services=s.BuildServiceProvider();Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();}
    private static AlarmTask Sample()=>new(){Id="period",Title="慢性處方箋領藥",ActivityStartAt=new(2026,10,1),ActivityEndAt=new(2026,10,11),ActivityAllDay=true,ScheduledAt=new(2026,10,1,9,0,0),Enabled=false,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
    [Fact] public async Task Period_roundtrips_database_backup_and_csv_without_enabling_notifications()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","測試");
        await Get<ISettingsRepository>().SaveAsync(new(){Key="TaskCalendarWeekStart",Value="1"});
        var task=Sample();var repo=Get<ITaskRepository>();await Get<ITaskService>().SaveLocalAsync(task);
        var read=(await repo.GetByIdAsync(task.Id))!;Assert.Equal(task.ActivityEndAt,read.ActivityEndAt);Assert.True(read.ActivityAllDay);Assert.False(read.Enabled);
        Assert.Null(await Get<ITaskSchedulingService>().GetNextOccurrenceAsync(read,new(2026,9,30)));
        var file=Path.Combine(DataDirectory,"activity.calbak");await Get<IBackupRestoreService>().CreateBackupAsync(file);
        await Get<Database>().ExecuteAsync("DELETE FROM Tasks WHERE Id='period';");
        await Get<ISettingsRepository>().SaveAsync(new(){Key="TaskCalendarWeekStart",Value="0"});
        await Get<IBackupRestoreService>().RestoreAsync(file);Assert.Equal(task.ActivityEndAt,(await repo.GetByIdAsync(task.Id))!.ActivityEndAt);
        Assert.Equal("1",(await Get<ISettingsRepository>().GetAsync("TaskCalendarWeekStart"))!.Value);
        var csv=LocalTaskCsvService.Export([task]);var preview=await Get<LocalTaskCsvService>().PreviewAsync(csv);
        var imported=Assert.Single(preview);Assert.True(imported.Valid,imported.Error);Assert.Equal(task.ActivityStartAt,imported.Task!.ActivityStartAt);Assert.Equal(task.ActivityEndAt,imported.Task.ActivityEndAt);Assert.False(imported.Task.Enabled);
        await Get<Database>().ExecuteAsync("DELETE FROM Tasks WHERE Id='period';");
        Assert.Equal(1,(await Get<LocalTaskCsvService>().ImportAsync(preview,false)).Imported);Assert.Equal(task.ActivityEndAt,(await repo.GetByIdAsync(task.Id))!.ActivityEndAt);
    }
    [Fact] public async Task V11_upgrade_preserves_reminders_and_adds_empty_activity_fields()
    {
        var task=Sample() with{ActivityStartAt=null,ActivityEndAt=null,ActivityAllDay=false};var repo=Get<ITaskRepository>();await repo.SaveLocalAsync(task);
        await Get<Database>().ExecuteAsync("ALTER TABLE Tasks DROP COLUMN ActivityStartAt; ALTER TABLE Tasks DROP COLUMN ActivityEndAt; ALTER TABLE Tasks DROP COLUMN ActivityAllDay; PRAGMA user_version=11;");
        await Get<IDatabaseInitializer>().InitializeAsync();var restored=(await repo.GetByIdAsync(task.Id))!;
        Assert.Equal(task.ScheduledAt,restored.ScheduledAt);Assert.Null(restored.ActivityStartAt);Assert.Null(restored.ActivityEndAt);Assert.False(restored.ActivityAllDay);
    }
    [Fact] public async Task Past_calendar_only_activity_is_allowed_but_invalid_ranges_are_rejected()
    {
        var task=Sample() with{ScheduledAt=DateTime.Today.AddDays(-20)};
        await Get<ITaskService>().SaveLocalAsync(task);
        foreach(var invalid in new[]{task with{ActivityEndAt=task.ActivityStartAt},task with{ActivityEndAt=null},task with{ActivityStartAt=task.ActivityStartAt!.Value.AddHours(1)},task with{Recurrence="Daily"}})
            await Assert.ThrowsAsync<ArgumentException>(()=>Get<ITaskService>().SaveLocalAsync(invalid));
        Assert.Equal(task.ActivityEndAt,(await Get<ITaskRepository>().GetByIdAsync(task.Id))!.ActivityEndAt);
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
}
