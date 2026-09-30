using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class CalendarReminderTests : IDisposable, IAppPaths
{
    public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CalendarReminderTests",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private readonly ServiceProvider services;
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    public CalendarReminderTests()
    {
        var collection=new ServiceCollection();collection.AddCore();collection.AddData();collection.AddSingleton<IAppPaths>(this);
        services=collection.BuildServiceProvider();Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private static AlarmTask Calendar(string source="Google:11111111111111111111111111111111")
    {
        var start=DateTime.Today.AddDays(2).AddHours(12);
        return new(){Id=source+":event",ExternalId="event",Source=source,Title="行程",ScheduledAt=start.AddMinutes(-10),
            CalendarStartAt=start,GoogleReminderAt=start.AddMinutes(-10),GoogleReminderEnabled=true,Enabled=true,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
    }
    [Fact]
    public async Task V10_upgrade_preserves_existing_tasks_until_next_calendar_sync()
    {
        var repo=Get<ITaskRepository>();var original=Calendar();await repo.ReplaceCloudCacheAsync(original.Source,[original]);
        await Get<Database>().ExecuteAsync("""
            ALTER TABLE Tasks DROP COLUMN CalendarStartAt;
            ALTER TABLE Tasks DROP COLUMN GoogleReminderEnabled;
            ALTER TABLE Tasks DROP COLUMN GoogleReminderAt;
            ALTER TABLE Tasks DROP COLUMN CalendarReminderEnabled;
            ALTER TABLE Tasks DROP COLUMN CalendarReminderAt;
            PRAGMA user_version=10;
            ALTER TABLE Tasks DROP COLUMN ActivityStartAt;
            ALTER TABLE Tasks DROP COLUMN ActivityEndAt;
            ALTER TABLE Tasks DROP COLUMN ActivityAllDay;
            """);
        await Get<IDatabaseInitializer>().InitializeAsync();
        var migrated=(await repo.GetByIdAsync(original.Id))!;
        Assert.Equal(original.ScheduledAt,migrated.ScheduledAt);Assert.True(migrated.Enabled);Assert.False(migrated.IsGoogleCalendar);
        await repo.ReplaceCloudCacheAsync(original.Source,[original]);Assert.True((await repo.GetByIdAsync(original.Id))!.IsGoogleCalendar);
    }
    [Theory]
    [InlineData("本機")] [InlineData("SheetA")] [InlineData("SheetB")] [InlineData("Google:33333333333333333333333333333333")]
    public async Task Reminder_editing_does_not_unlock_non_calendar_tasks(string source)
    {
        var repo=Get<ITaskRepository>();var task=Calendar(source) with{CalendarStartAt=null,GoogleReminderAt=null,GoogleReminderEnabled=null};
        if(source==TaskSources.Local)await repo.SaveLocalAsync(task);else await repo.ReplaceCloudCacheAsync(source,[task]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<ITaskService>().SaveCalendarReminderAsync(task.Id,false,null));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<ITaskService>().ResetCalendarReminderAsync(task.Id));
        Assert.True((await repo.GetByIdAsync(task.Id))!.Enabled);
    }
    [Fact]
    public async Task Active_notification_blocks_editing_and_reset_until_completed()
    {
        var task=Calendar();var repo=Get<ITaskRepository>();await repo.ReplaceCloudCacheAsync(task.Source,[task]);
        var runtime=Get<IRuntimeStore>();var id=OccurrenceIdentity.For(task.Id,task.ScheduledAt);
        Assert.True(await runtime.ClaimAsync(id,task,task.ScheduledAt));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.SaveCalendarReminderAsync(task.Id,false,null));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.ResetCalendarReminderAsync(task.Id));
        await runtime.CompleteAsync(id);await repo.SaveCalendarReminderAsync(task.Id,false,null);
        Assert.False((await repo.GetByIdAsync(task.Id))!.Enabled);
    }
    [Fact]
    public async Task Same_event_ids_in_separate_sources_keep_independent_overrides_and_local_copies_are_clean()
    {
        var repo=Get<ITaskRepository>();var personal=Calendar();var company=Calendar("Google:22222222222222222222222222222222");
        await repo.ReplaceCloudCacheAsync(personal.Source,[personal]);await repo.ReplaceCloudCacheAsync(company.Source,[company]);
        await repo.SaveCalendarReminderAsync(personal.Id,false,personal.ScheduledAt.AddHours(-1));
        await repo.ReplaceCloudCacheAsync(company.Source,[company with{ScheduledAt=company.ScheduledAt.AddHours(1),GoogleReminderAt=company.ScheduledAt.AddHours(1)}]);
        var edited=(await repo.GetByIdAsync(personal.Id))!;Assert.False(edited.Enabled);Assert.True(edited.HasCalendarReminderOverride);
        var untouched=(await repo.GetByIdAsync(company.Id))!;Assert.True(untouched.Enabled);Assert.False(untouched.HasCalendarReminderOverride);
        await repo.SaveLocalAsync(edited with{Id="copy",Source=TaskSources.Local,ExternalId=null});
        var copy=(await repo.GetByIdAsync("copy"))!;Assert.False(copy.IsGoogleCalendar);Assert.Null(copy.CalendarReminderAt);Assert.Null(copy.GoogleReminderAt);
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
}
