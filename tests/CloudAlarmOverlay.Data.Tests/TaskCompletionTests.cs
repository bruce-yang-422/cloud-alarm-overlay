using System.IO.Compression;
using System.Text.Json;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class TaskCompletionTests:IDisposable,IAppPaths
{
    public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmTaskProgress",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private readonly ServiceProvider services;
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    public TaskCompletionTests()
    {
        var collection=new ServiceCollection();collection.AddCore();collection.AddData();collection.AddSingleton<IAppPaths>(this);
        services=collection.BuildServiceProvider();Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private static AlarmTask Sample(string id)=>new(){Id=id,Title=id,ScheduledAt=DateTime.Today.AddHours(13),CreatedAt=DateTime.Today.AddDays(-2),UpdatedAt=DateTime.Now};
    [Fact] public async Task Completion_survives_cloud_refresh_and_does_not_modify_the_task()
    {
        var tasks=Get<ITaskRepository>();var progress=Get<ITaskCompletionRepository>();
        var task=Sample("cloud") with{Source=TaskSources.SheetB,ExternalId="cloud"};
        await tasks.ReplaceCloudCacheAsync(task.Source,[task]);await progress.CompleteAsync(task,task.ScheduledAt);await progress.CompleteAsync(task,task.ScheduledAt);
        var updated=task with{Description="來源更新"};await tasks.ReplaceCloudCacheAsync(task.Source,[updated]);
        Assert.Equal(updated,await tasks.GetByIdAsync(task.Id));Assert.True(await progress.IsCompletedAsync(updated,task.ScheduledAt));Assert.Single(await progress.GetAllAsync());
        Assert.True(await progress.IsCompletedAsync(updated,DateTime.SpecifyKind(task.ScheduledAt,DateTimeKind.Unspecified)));
        await progress.RestoreAsync(task.Source,task.Id,task.ScheduledAt);Assert.False(await progress.IsCompletedAsync(task,task.ScheduledAt));
    }
    [Fact] public async Task Recurring_completion_skips_only_one_occurrence_and_undo_restores_schedule()
    {
        var task=Sample("routine") with{ScheduledAt=DateTime.Today.AddDays(-30).AddHours(13),Recurrence="Daily"};
        var at=DateTime.Today.AddHours(13);var progress=Get<ITaskCompletionRepository>();var scheduler=Get<ITaskSchedulingService>();
        await progress.CompleteAsync(task,at);
        Assert.Equal(at.AddDays(1),await scheduler.GetNextOccurrenceAsync(task,at.AddTicks(-1)));
        await progress.RestoreAsync(task.Source,task.Id,at);
        Assert.Equal(at,await scheduler.GetNextOccurrenceAsync(task,at.AddTicks(-1)));
        Assert.Null(await scheduler.GetNextOccurrenceAsync(task with{Enabled=false},at.AddTicks(-1)));
    }
    [Fact] public async Task Google_completion_uses_event_identity_when_reminder_changes_and_keeps_private_text_out_of_snapshot()
    {
        var task=Sample("private") with{Source="Google:"+Guid.NewGuid().ToString("N"),ExternalId="event",CalendarStartAt=DateTime.Today.AddDays(1),GoogleReminderAt=DateTime.Today,GoogleReminderEnabled=true,Title="私人醫療",Description="私人內容",Note="私人網址"};
        var progress=Get<ITaskCompletionRepository>();await progress.CompleteAsync(task,task.ScheduledAt);
        Assert.True(await progress.IsCompletedAsync(task with{ScheduledAt=task.ScheduledAt.AddHours(2)},task.ScheduledAt.AddHours(2)));
        var record=Assert.Single(await progress.GetAllAsync());
        Assert.DoesNotContain("私人醫療",record.SnapshotJson);Assert.DoesNotContain("私人內容",record.SnapshotJson);Assert.DoesNotContain("私人網址",record.SnapshotJson);
        await progress.DeleteSourceAsync(task.Source);Assert.Empty(await progress.GetAllAsync());
    }
    [Fact] public async Task Backup_restores_local_completion_and_excludes_private_cloud_completions()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("PROGRESS","測試",UsageModes.Personal);
        var local=Sample("local");var cloud=Sample("cloud") with{Source="Google:"+Guid.NewGuid().ToString("N"),ExternalId="cloud"};
        var tasks=Get<ITaskRepository>();var progress=Get<ITaskCompletionRepository>();
        await tasks.SaveLocalAsync(local);await tasks.ReplaceCloudCacheAsync(cloud.Source,[cloud]);
        await progress.CompleteAsync(local,local.ScheduledAt);await progress.CompleteAsync(cloud,cloud.ScheduledAt);
        var path=Path.Combine(DataDirectory,"progress.calbak");await Get<IBackupRestoreService>().CreateBackupAsync(path);
        using(var archive=ZipFile.OpenRead(path))
        using(var reader=new StreamReader(archive.GetEntry("task-completions.json")!.Open()))
            Assert.Equal(local.Id,Assert.Single(JsonSerializer.Deserialize<TaskCompletion[]>(await reader.ReadToEndAsync())!).TaskId);
        await tasks.DeleteLocalAsync(local.Id);Assert.False(await progress.IsCompletedAsync(local,local.ScheduledAt));
        await Get<IBackupRestoreService>().RestoreAsync(path);Assert.True(await progress.IsCompletedAsync(local,local.ScheduledAt));
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
}
