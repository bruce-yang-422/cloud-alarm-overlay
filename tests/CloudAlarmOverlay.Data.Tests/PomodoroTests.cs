using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
namespace CloudAlarmOverlay.Data.Tests;
public sealed class PomodoroTests:IDisposable
{
    private readonly Paths paths=new();
    private readonly Clock clock=new();
    private readonly ServiceProvider services;
    private IPomodoroService Timer=>services.GetRequiredService<IPomodoroService>();
    private IPomodoroRepository Repo=>services.GetRequiredService<IPomodoroRepository>();
    public PomodoroTests()
    {
        var c=new ServiceCollection().AddCore().AddData();
        c.AddSingleton<IAppPaths>(paths);c.AddSingleton<TimeProvider>(clock);
        services=c.BuildServiceProvider();
        services.GetRequiredService<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
        Timer.InitializeAsync().GetAwaiter().GetResult();
    }
    private Task<IReadOnlyList<PomodoroLogEntry>> Logs()=>Repo.GetLogsAsync(DateTime.MinValue,DateTime.MaxValue);
    [Fact] public async Task Pause_preserves_remaining_and_resume_completes_once_before_ack_starts_break()
    {
        await Timer.StartAsync();clock.Advance(5);await Timer.PauseAsync();
        Assert.Equal(TimeSpan.FromMinutes(20),Timer.State.Remaining);
        clock.Advance(60);await Timer.TickAsync();Assert.Equal("Paused",Timer.State.Status);
        await Timer.StartAsync();clock.Advance(20);
        await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Timer.TickAsync()));
        Assert.Equal("AwaitingConfirmation",Timer.State.Status);
        var log=Assert.Single(await Logs());Assert.Equal("Completed",log.Result);Assert.False(log.IsActive);
        await Timer.TickAsync();Assert.Single(await Logs());
        await Timer.ConfirmAsync();Assert.Equal("Break",Timer.State.Phase);
        clock.Advance(5);await Timer.TickAsync();await Timer.ConfirmAsync();
        Assert.Equal("Idle",Timer.State.Status);Assert.Equal(2,(await Logs()).Count);
    }
    [Fact] public async Task Long_break_follows_completed_cycles_and_skip_does_not_award_a_round()
    {
        await Timer.SaveOptionsAsync(new(){FocusMinutes=5,Interval=2});
        await Timer.StartAsync();await Timer.SkipAsync();await Timer.ConfirmAsync();
        Assert.Equal(0,Timer.State.Cycle);
        clock.Advance(5);await Timer.TickAsync();await Timer.ConfirmAsync();
        for(var i=0;i<2;i++)
        {
            await Timer.StartAsync();clock.Advance(5);await Timer.TickAsync();await Timer.ConfirmAsync();
            Assert.Equal(i==0?"Break":"LongBreak",Timer.State.Phase);
            clock.Advance(i==0?5:15);await Timer.TickAsync();await Timer.ConfirmAsync();
        }
        Assert.Equal(2,(await Logs()).Count(x=>x.Type=="Focus"&&x.Result=="Completed"));
        Assert.Equal(0,Timer.State.Cycle);
    }
    [Fact] public async Task Reset_records_actual_end_but_keeps_completed_null_and_settings_are_idle_only()
    {
        await Timer.StartAsync();clock.Advance(2);await Timer.PauseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Timer.SaveOptionsAsync(new()));
        await Timer.ResetAsync();
        var row=Assert.Single(await Logs());Assert.Null(row.CompletedAt);Assert.NotNull(row.EndedAt);
        Assert.Equal(25,row.PlannedMinutes);Assert.Equal("Interrupted",row.Result);Assert.False(row.IsActive);
        Assert.Equal("Idle",Timer.State.Status);
    }
    [Fact] public async Task Restart_recovers_active_session_without_fabricating_end_time()
    {
        await Timer.StartAsync();
        var c=new ServiceCollection().AddCore().AddData();c.AddSingleton<IAppPaths>(paths);
        using var next=c.BuildServiceProvider();
        var timer=next.GetRequiredService<IPomodoroService>();await timer.InitializeAsync();
        var log=Assert.Single(await Logs());Assert.False(log.IsActive);Assert.Null(log.EndedAt);
        Assert.Equal("Interrupted",log.Result);Assert.Equal("Idle",timer.State.Status);
    }
    [Fact] public async Task Pomodoro_does_not_write_task_history_or_general_settings_and_options_persist()
    {
        await Timer.SaveOptionsAsync(new(){FocusMinutes=30,SoundEnabled=false,SoundName="test"});
        await Timer.StartAsync();clock.Advance(30);await Timer.TickAsync();await Timer.ConfirmAsync();
        var db=services.GetRequiredService<Database>();
        Assert.Equal(0,Assert.Single(await db.QueryAsync<int>("SELECT COUNT(*) FROM AcknowledgementLogs")));
        Assert.Equal(0,Assert.Single(await db.QueryAsync<int>("SELECT COUNT(*) FROM Settings")));
        Assert.Contains("30",Assert.Single(await Repo.GetSettingsAsync()).Value);
    }
    [Fact] public async Task Invalid_options_leave_previous_configuration_intact()
    {
        await Assert.ThrowsAsync<ArgumentException>(()=>Timer.SaveOptionsAsync(new(){FocusMinutes=0}));
        Assert.Equal(25,Timer.Options.FocusMinutes);Assert.Empty(await Repo.GetSettingsAsync());
    }
    [Fact] public async Task Concurrent_start_creates_one_log_and_reset_prevents_stale_confirmation()
    {
        await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Timer.StartAsync()));
        Assert.Single(await Logs());await Timer.SkipAsync();await Timer.ResetAsync();await Timer.ConfirmAsync();
        Assert.Equal("Idle",Timer.State.Status);Assert.Single(await Logs());
    }
    [Fact] public async Task V3_upgrade_preserves_existing_completed_log_and_settings()
    {
        var db=services.GetRequiredService<Database>();
        await db.ExecuteAsync("""
            DROP INDEX IX_PomodoroLog_StartedAt;
            ALTER TABLE PomodoroLog DROP COLUMN EndedAt;
            ALTER TABLE PomodoroLog DROP COLUMN PlannedMinutes;
            ALTER TABLE PomodoroLog DROP COLUMN IsActive;
            INSERT INTO PomodoroLog(Id,Type,StartedAt,CompletedAt,Result)
            VALUES('legacy','Focus','2026-09-14T08:00:00','2026-09-14T08:25:00','Completed');
            INSERT INTO PomodoroSettings(Key,Value) VALUES('legacy','keep');
            PRAGMA user_version=3;
            """);
        await services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        var row=Assert.Single(await Logs());Assert.Equal(row.CompletedAt,row.EndedAt);
        Assert.False(row.IsActive);Assert.Equal(0,row.PlannedMinutes);
        Assert.Equal("keep",Assert.Single(await Repo.GetSettingsAsync()).Value);
        Assert.Equal(5,Assert.Single(await db.QueryAsync<int>("PRAGMA user_version")));
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
    private sealed class Clock:TimeProvider
    {
        private DateTimeOffset now=new(2026,9,15,0,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>now;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>now.UtcTicks;
        public void Advance(int minutes)=>now=now.AddMinutes(minutes);
    }
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmPomodoroTests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        public string LogDirectory=>Path.Combine(DataDirectory,"logs");
    }
}
