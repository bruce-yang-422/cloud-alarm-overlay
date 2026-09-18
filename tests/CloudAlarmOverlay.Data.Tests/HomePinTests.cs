using System.IO.Compression;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class HomePinTests : IDisposable, IAppPaths
{
    public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmHomePinTests",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private readonly ServiceProvider services;
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    public HomePinTests()
    {
        var collection=new ServiceCollection();
        collection.AddCore();collection.AddData();collection.AddSingleton<IAppPaths>(this);
        services=collection.BuildServiceProvider();
        Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private static AlarmTask TaskItem(string id)=>new() {Id=id,Title=id,ScheduledAt=DateTime.Today.AddDays(1),CreatedAt=DateTime.Today,UpdatedAt=DateTime.Today};
    private static CountdownItem Counter(string id)=>new() {Id=id,Title=id,TargetAt=DateTime.Today.AddDays(2),CreatedAt=DateTime.Today,IsPinned=true};

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Configurable_limit_is_shared_and_reducing_it_keeps_existing_pins(int limit)
    {
        var settings=Get<ISettingsRepository>();var pins=Get<ITaskHomePinRepository>();
        var tasks=Get<ITaskRepository>();var counters=Get<ICountdownRepository>();
        Assert.Equal(2,await pins.GetLimitAsync());
        await settings.SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value=limit.ToString()});
        for(var i=0;i<limit;i++)
        {
            if(i%2==0){await tasks.SaveLocalAsync(TaskItem("t"+i));await pins.SetPinnedAsync("t"+i,true);}
            else await counters.SaveAsync(Counter("c"+i));
        }
        await tasks.SaveLocalAsync(TaskItem("extra"));
        var taskError=await Assert.ThrowsAsync<InvalidOperationException>(()=>pins.SetPinnedAsync("extra",true));
        Assert.Contains($"最多釘選 {limit}",taskError.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>counters.SaveAsync(Counter("extra")));
        if(limit>2)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>settings.SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value="2"}));
            Assert.Equal(limit,await pins.GetLimitAsync());
        }
        await pins.SetPinnedAsync("t0",false);
        await counters.SaveAsync(Counter("replacement"));
        Assert.Equal(limit,(await pins.GetTaskIdsAsync()).Count+(await counters.GetAllAsync()).Count(c=>c.IsPinned));
        await Get<IDatabaseInitializer>().InitializeAsync();
        Assert.Equal(limit,await pins.GetLimitAsync());
    }

    [Theory]
    [InlineData("1")] [InlineData("6")] [InlineData("abc")] [InlineData("")]
    public async Task Invalid_limits_cannot_be_saved(string value)
    {
        await Assert.ThrowsAsync<ArgumentException>(()=>Get<ISettingsRepository>().SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value=value}));
        Assert.Equal(2,await Get<ITaskHomePinRepository>().GetLimitAsync());
    }

    [Fact]
    public async Task Backup_restores_limit_and_never_shrinks_below_existing_pins()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","示範");
        var settings=Get<ISettingsRepository>();var pins=Get<ITaskHomePinRepository>();var counters=Get<ICountdownRepository>();
        var backup=Get<IBackupRestoreService>();var file=Path.Combine(DataDirectory,"limit.calbak");
        await settings.SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value="3"});
        await backup.CreateBackupAsync(file);
        await settings.SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value="5"});
        await backup.RestoreAsync(file);Assert.Equal(3,await pins.GetLimitAsync());
        await settings.SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value="5"});
        for(var i=0;i<5;i++)await counters.SaveAsync(Counter("existing"+i));
        await backup.RestoreAsync(file);
        Assert.Equal(5,await pins.GetLimitAsync());Assert.Equal(5,(await counters.GetAllAsync()).Count(c=>c.IsPinned));
    }

    [Fact]
    public async Task Lowering_limit_and_adding_a_pin_are_serialized()
    {
        var settings=Get<ISettingsRepository>();var pins=Get<ITaskHomePinRepository>();var counters=Get<ICountdownRepository>();
        await settings.SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value="3"});
        await counters.SaveAsync(Counter("a"));await counters.SaveAsync(Counter("b"));
        await Get<ITaskRepository>().SaveLocalAsync(TaskItem("third"));
        static async Task<bool> Attempt(Func<Task> action){try{await action();return true;}catch(InvalidOperationException){return false;}}
        var results=await Task.WhenAll(Task.Run(()=>Attempt(()=>settings.SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value="2"}))),Task.Run(()=>Attempt(()=>pins.SetPinnedAsync("third",true))));
        Assert.Single(results,r=>r);
        Assert.True((await pins.GetTaskIdsAsync()).Count+2<=await pins.GetLimitAsync());
    }

    [Fact]
    public async Task Mixed_pins_share_capacity_in_both_directions_and_unpin_preserves_source()
    {
        var tasks=Get<ITaskRepository>();var pins=Get<ITaskHomePinRepository>();var counters=Get<ICountdownRepository>();
        await tasks.SaveLocalAsync(TaskItem("a"));await tasks.SaveLocalAsync(TaskItem("b"));
        await pins.SetPinnedAsync("a",true);await counters.SaveAsync(Counter("c"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>pins.SetPinnedAsync("b",true));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>counters.SaveAsync(Counter("d")));
        await pins.SetPinnedAsync("a",true); // Existing pin does not consume another slot.
        await counters.SaveAsync(Counter("c") with {Title="修改標題"});
        await pins.SetPinnedAsync("a",false);await pins.SetPinnedAsync("b",true);
        Assert.Equal(TaskItem("a"),await tasks.GetByIdAsync("a"));
        await tasks.DeleteLocalAsync("b");Assert.Empty(await pins.GetTaskIdsAsync());
        await counters.SaveAsync(Counter("d"));
        await Get<IDatabaseInitializer>().InitializeAsync();
        Assert.Equal(2,(await counters.GetAllAsync()).Count(i=>i.IsPinned));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>pins.SetPinnedAsync("missing",true));
    }

    [Fact]
    public async Task Cloud_refresh_preserves_pin_without_modifying_task_and_removal_releases_slot()
    {
        var tasks=Get<ITaskRepository>();var pins=Get<ITaskHomePinRepository>();
        var cloud=TaskItem("cloud") with {Source=TaskSources.SheetB,ExternalId="shared"};
        await tasks.ReplaceCloudCacheAsync(TaskSources.SheetB,[cloud]);await pins.SetPinnedAsync(cloud.Id,true);
        Assert.Equal(cloud,await tasks.GetByIdAsync(cloud.Id));
        var updated=cloud with {Title="雲端更新",ScheduledAt=cloud.ScheduledAt.AddDays(1)};
        await tasks.ReplaceCloudCacheAsync(TaskSources.SheetB,[updated]);
        Assert.Equal(cloud.Id,Assert.Single(await pins.GetTaskIdsAsync()));
        Assert.Equal(updated,await tasks.GetByIdAsync(cloud.Id));
        await tasks.ReplaceCloudCacheAsync(TaskSources.SheetB,[]);
        Assert.Empty(await pins.GetTaskIdsAsync());
    }

    [Fact]
    public async Task Concurrent_task_and_countdown_pin_attempts_cannot_exceed_limit()
    {
        var pins=Get<ITaskHomePinRepository>();var counters=Get<ICountdownRepository>();
        await Get<ITaskRepository>().SaveLocalAsync(TaskItem("task"));await counters.SaveAsync(Counter("first"));
        static async Task<bool> Attempt(Func<Task> action) {try {await action();return true;}catch(InvalidOperationException){return false;}}
        var results=await Task.WhenAll(Task.Run(()=>Attempt(()=>pins.SetPinnedAsync("task",true))),Task.Run(()=>Attempt(()=>counters.SaveAsync(Counter("second")))));
        Assert.Single(results,success=>success);
        Assert.Equal(2,(await pins.GetTaskIdsAsync()).Count+(await counters.GetAllAsync()).Count(i=>i.IsPinned));
    }

    [Fact]
    public async Task Version_eight_upgrade_preserves_countdown_pins()
    {
        await Get<ICountdownRepository>().SaveAsync(Counter("existing"));
        await Get<Database>().ExecuteAsync("DROP TABLE TaskHomePins; PRAGMA user_version=8;");
        await Get<IDatabaseInitializer>().InitializeAsync();
        Assert.True(Assert.Single(await Get<ICountdownRepository>().GetAllAsync()).IsPinned);
        Assert.Empty(await Get<ITaskHomePinRepository>().GetTaskIdsAsync());
    }

    [Fact]
    public async Task Backup_roundtrip_restores_local_task_pins_and_obeys_existing_capacity()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","示範");
        var tasks=Get<ITaskRepository>();var pins=Get<ITaskHomePinRepository>();var counters=Get<ICountdownRepository>();
        var backup=Get<IBackupRestoreService>();var file=Path.Combine(DataDirectory,"pins.calbak");
        await tasks.SaveLocalAsync(TaskItem("saved"));await pins.SetPinnedAsync("saved",true);await counters.SaveAsync(Counter("saved-counter"));
        await backup.CreateBackupAsync(file);
        await tasks.DeleteLocalAsync("saved");await counters.DeleteAsync("saved-counter");
        await backup.RestoreAsync(file);
        Assert.Equal("saved",Assert.Single(await pins.GetTaskIdsAsync()));
        Assert.True(Assert.Single(await counters.GetAllAsync()).IsPinned);
        await tasks.DeleteLocalAsync("saved");await counters.DeleteAsync("saved-counter");
        await tasks.SaveLocalAsync(TaskItem("current-a"));await tasks.SaveLocalAsync(TaskItem("current-b"));
        await pins.SetPinnedAsync("current-a",true);await pins.SetPinnedAsync("current-b",true);
        await backup.RestoreAsync(file);
        Assert.Equal(2,(await pins.GetTaskIdsAsync()).Count);
        Assert.NotNull(await tasks.GetByIdAsync("saved"));Assert.False(Assert.Single(await counters.GetAllAsync()).IsPinned);
    }

    [Fact]
    public async Task Older_backup_without_task_pins_remains_readable()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","示範");
        var file=Path.Combine(DataDirectory,"old.calbak");var backup=Get<IBackupRestoreService>();
        await Get<ICountdownRepository>().SaveAsync(Counter("old"));await backup.CreateBackupAsync(file);
        using(var archive=ZipFile.Open(file,ZipArchiveMode.Update))
        {
            archive.GetEntry("task-pins.json")!.Delete();archive.GetEntry("manifest.json")!.Delete();
            using var writer=new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write("{\"Format\":\"CloudAlarmOverlay\",\"Version\":2}");
        }
        await Get<ICountdownRepository>().DeleteAsync("old");await backup.RestoreAsync(file);
        Assert.True(Assert.Single(await Get<ICountdownRepository>().GetAllAsync()).IsPinned);
    }

    public void Dispose(){services.Dispose();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
}
