using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class HealthToolsTests
{
    private readonly Clock clock = new();
    private readonly Settings settings = new();
    private readonly HealthToolsService health;
    public HealthToolsTests() => health = new(settings, clock);
    private static readonly PomodoroState Focus = new("Focus", "Running", TimeSpan.FromMinutes(25));
    private HealthToolState Screen => health.Snapshot.Tools.Single(t => t.Options.Kind == "Screen");
    private Task Enable(string kind = "Screen", int minutes = 20) => health.SaveToolAsync(new() { Kind = kind, Enabled = true, IntervalMinutes = minutes, Start = new(10, 0), End = new(18, 0), Days = 127 });
    private async Task AdvanceTo(int hour, int minute)
    {
        var target = clock.At.Date.AddHours(hour).AddMinutes(minute);
        while (clock.At < target) { clock.At = clock.At.AddSeconds(10); await health.TickAsync(); }
    }
    [Fact] public async Task Defaults_are_disabled_and_enabling_mid_cycle_keeps_daily_anchor()
    {
        await health.InitializeAsync(); Assert.All(health.Snapshot.Tools, t => Assert.False(t.Options.Enabled));
        clock.At = clock.At.AddMinutes(7); await Enable();
        Assert.Equal(clock.At.Date.AddHours(10).AddMinutes(20), Screen.NextAt);
    }
    [Fact] public async Task Focus_defers_and_confirmation_releases_without_shifting_next_occurrence()
    {
        await Enable(); await AdvanceTo(10, 20);
        Assert.Empty(health.Ready(Focus, false, false));
        await AdvanceTo(10, 25);
        var item = Assert.Single(health.Ready(new("Focus", "AwaitingConfirmation"), false, false));
        Assert.Equal(20, item.ScheduledAt.Minute);
        await health.MarkDisplayedAsync([item.Id], "break");
        Assert.Null(Screen.Pending); Assert.Equal(40, Screen.NextAt!.Value.Minute);
        var entry = Assert.Single(health.Snapshot.Events); Assert.Equal(25, entry.TriggeredAt!.Value.Minute);
        Assert.Null(entry.AcknowledgedAt);
    }
    [Fact] public async Task Repeated_due_items_do_not_extend_first_pending_deadline()
    {
        await Enable(); await AdvanceTo(10, 40);
        Assert.Equal(20, Screen.Pending!.ScheduledAt.Minute);
        Assert.Empty(health.Ready(Focus, false, false));
        await AdvanceTo(10, 50);
        Assert.Single(health.Ready(Focus, false, false));
        Assert.Equal(11, Screen.NextAt!.Value.Hour);
    }
    [Theory]
    [InlineData("Focus", "Paused")]
    [InlineData("Focus", "Idle")]
    [InlineData("Focus", "AwaitingConfirmation")]
    [InlineData("Break", "Running")]
    [InlineData("LongBreak", "Running")]
    public async Task Non_focus_running_states_release_pending(string phase, string status)
    {
        await Enable(); await AdvanceTo(10, 20);
        Assert.Single(health.Ready(new(phase, status), false, false));
    }
    [Fact] public async Task Disabling_coordination_releases_pending_during_focus()
    {
        await Enable(); await AdvanceTo(10, 20); await health.SetCoordinationAsync(false);
        Assert.Single(health.Ready(Focus, false, false));
    }
    [Fact] public async Task Shared_preferences_persist_side_and_alignment_together_and_reject_invalid_side()
    {
        await health.InitializeAsync(); Assert.Equal("Right", health.Snapshot.NotificationSide);
        await health.SavePreferencesAsync(false, "Left");
        var restarted = new HealthToolsService(settings, clock); await restarted.InitializeAsync();
        Assert.Equal("Left", restarted.Snapshot.NotificationSide); Assert.False(restarted.Snapshot.CoordinatePomodoro);
        await Assert.ThrowsAsync<ArgumentException>(() => health.SavePreferencesAsync(true, "invalid"));
        Assert.Equal("Left", health.Snapshot.NotificationSide); Assert.False(health.Snapshot.CoordinatePomodoro);
    }
    [Fact] public async Task Quiet_and_unavailable_override_maximum_deferral()
    {
        await Enable(); await AdvanceTo(10, 50);
        Assert.Empty(health.Ready(Focus, true, false)); Assert.Empty(health.Ready(Focus, false, true));
        Assert.Single(health.Ready(Focus, false, false));
    }
    [Theory]
    [InlineData("Break")]
    [InlineData("LongBreak")]
    public async Task Auto_align_requests_all_active_tools_once_then_anchors_at_first_break_end(string breakPhase)
    {
        await Enable(); await Enable("Water", 60); await Enable("Stretch", 90);
        await health.SavePreferencesAsync("AutoAlign", "Center");
        await health.ObservePomodoroAsync(Focus);
        await AdvanceTo(10, 20); Assert.Null(Screen.Pending); Assert.Empty(health.Ready(Focus, false, false));
        await AdvanceTo(10, 25);
        await health.ObservePomodoroAsync(new("Focus", "AwaitingConfirmation"));
        var ready = health.Ready(new("Focus", "AwaitingConfirmation"), false, false);
        Assert.Equal(3, ready.Length); Assert.All(ready, p => Assert.Equal(clock.At, p.ScheduledAt));
        await health.MarkDisplayedAsync(ready.Select(p => p.Id), "align");
        await health.ObservePomodoroAsync(new("Focus", "AwaitingConfirmation")); Assert.Null(Screen.Pending);
        await health.ObservePomodoroAsync(new(breakPhase, "Running"));
        await AdvanceTo(10, 30); Assert.Null(Screen.Anchor);
        await health.ObservePomodoroAsync(new(breakPhase, "AwaitingConfirmation"));
        Assert.Equal(clock.At, Screen.Anchor); Assert.Equal(clock.At.AddMinutes(20), Screen.NextAt);
        Assert.Equal(clock.At.AddMinutes(60), health.Snapshot.Tools.Single(t => t.Options.Kind == "Water").NextAt);
        await health.ObservePomodoroAsync(new("Focus", "Running", Round: 2));
        await AdvanceTo(10, 50);
        Assert.Single(health.Ready(Focus, false, false)); // Subsequent periods run independently.
        await health.MarkDisplayedAsync([Screen.Pending!.Id], "periodic");
        await AdvanceTo(10, 55); await health.ObservePomodoroAsync(new("Focus", "AwaitingConfirmation", Round: 2));
        Assert.Empty(health.Ready(new(), false, false)); Assert.Equal(3, health.Snapshot.Events.Count(e => e.BatchId == "align"));
        Assert.Equal(clock.At.Date.AddHours(11).AddMinutes(10), Screen.NextAt);
        var restarted = new HealthToolsService(settings, clock); await restarted.InitializeAsync();
        Assert.Equal(Screen.NextAt, restarted.Snapshot.Tools.Single(t => t.Options.Kind == "Screen").NextAt);
        Assert.Equal("Center", restarted.Snapshot.NotificationSide); Assert.Equal("AutoAlign", restarted.Snapshot.EffectiveMode);
        await health.ObservePomodoroAsync(new()); await health.ObservePomodoroAsync(Focus);
        Assert.Equal("Focus", health.Snapshot.AlignmentStage);
    }
    [Fact] public async Task Auto_align_respects_enabled_hours_pause_and_failed_commit()
    {
        await Enable(); await health.SavePreferencesAsync("AutoAlign", "Right");
        await health.ObservePomodoroAsync(Focus); await AdvanceTo(10, 25);
        settings.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => health.ObservePomodoroAsync(new("Focus", "AwaitingConfirmation")));
        Assert.Equal("Focus", health.Snapshot.AlignmentStage); Assert.Null(Screen.Pending);
        settings.Fail = false; await health.ObservePomodoroAsync(new("Focus", "AwaitingConfirmation"));
        Assert.Single(health.Ready(new(), false, false)); Assert.Empty(health.Ready(new(), true, false));
        await health.SetPausedAsync(true); await health.ObservePomodoroAsync(new()); await health.ObservePomodoroAsync(Focus);
        await health.ObservePomodoroAsync(new("Focus", "AwaitingConfirmation"));
        Assert.Empty(health.Snapshot.AlignmentKinds); Assert.Null(Screen.Pending);
    }
    [Theory]
    [InlineData(true, "Delay")]
    [InlineData(false, "Override")]
    public async Task Legacy_boolean_migrates_without_changing_behavior(bool enabled, string expected)
    {
        await settings.SaveAsync(new() { Key = HealthToolsService.SettingsKey, Value = System.Text.Json.JsonSerializer.Serialize(new HealthToolsState { CoordinatePomodoro = enabled }) });
        await health.InitializeAsync(); Assert.Equal(expected, health.Snapshot.EffectiveMode);
        await Assert.ThrowsAsync<ArgumentException>(() => health.SavePreferencesAsync("invalid", "Center"));
        Assert.Equal(expected, health.Snapshot.EffectiveMode);
    }
    [Fact] public async Task Merged_batch_counts_once_and_concurrent_delivery_is_idempotent()
    {
        await Enable(); await Enable("Water"); await AdvanceTo(10, 20);
        var ids = health.Ready(new(), false, false).Select(p => p.Id).ToArray(); Assert.Equal(2, ids.Length);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => health.MarkDisplayedAsync(ids, "batch")));
        Assert.Equal(2, health.Snapshot.Events.Length); Assert.Single(health.Snapshot.Events.Select(e => e.BatchId).Distinct());
        await health.AcknowledgeAsync("batch"); Assert.All(health.Snapshot.Events, e => Assert.NotNull(e.AcknowledgedAt));
    }
    [Fact] public async Task Recent_display_suppresses_close_occurrence_but_keeps_next_grid_time()
    {
        await Enable(); await AdvanceTo(10, 39);
        await health.MarkDisplayedAsync([Screen.Pending!.Id], "late");
        await AdvanceTo(10, 40);
        Assert.Null(Screen.Pending); Assert.Equal(clock.At.Date.AddHours(11), Screen.NextAt);
        Assert.Contains(health.Snapshot.Events, e => e.Result == "近期已提醒，略過");
    }
    [Fact] public async Task Restart_preserves_configuration_history_and_pause_without_replaying_pending()
    {
        await Enable(); await AdvanceTo(10, 20); await health.MarkDisplayedAsync([Screen.Pending!.Id], "one");
        await AdvanceTo(10, 40); await health.SetPausedAsync(true);
        var restarted = new HealthToolsService(settings, clock); await restarted.InitializeAsync();
        Assert.True(restarted.IsPaused); Assert.True(restarted.Snapshot.Tools.Single(t => t.Options.Kind == "Screen").Options.Enabled);
        Assert.All(restarted.Snapshot.Tools, t => Assert.Null(t.Pending));
        Assert.Single(restarted.Snapshot.Events, e => e.TriggeredAt is not null);
    }
    [Fact] public async Task Sleep_gap_and_lock_clear_backlog_and_schedule_next_future_slot()
    {
        await Enable(); await AdvanceTo(10, 20);
        clock.At = clock.At.AddHours(1); await health.TickAsync();
        Assert.Null(Screen.Pending); Assert.Equal(clock.At.Date.AddHours(11).AddMinutes(40), Screen.NextAt);
        await AdvanceTo(11, 40); Assert.NotNull(Screen.Pending);
        await health.TickAsync(true); Assert.Null(Screen.Pending);
        await health.TickAsync(false); Assert.Empty(health.Ready(new(), false, false));
    }
    [Fact] public async Task End_of_window_expires_pending_and_daily_pause_resumes_next_day()
    {
        await health.SaveToolAsync(new() { Kind = "Screen", Enabled = true, IntervalMinutes = 20, Start = new(10, 0), End = new(11, 0), Days = 127 });
        await AdvanceTo(11, 0); Assert.Null(Screen.Pending);
        await health.SetPausedAsync(true); clock.At = clock.At.AddDays(1); await health.TickAsync();
        Assert.False(health.IsPaused); Assert.Empty(health.Ready(new(), false, false));
    }
    [Fact] public async Task Changing_or_disabling_tool_clears_pending_and_failed_save_keeps_snapshot()
    {
        await Enable(); await AdvanceTo(10, 20);
        var previous = health.Snapshot; settings.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => health.SaveToolAsync(Screen.Options with { Enabled = false }));
        Assert.Same(previous, health.Snapshot); settings.Fail = false;
        await health.SaveToolAsync(Screen.Options with { Enabled = false }); Assert.Null(Screen.Pending); Assert.Null(Screen.NextAt);
    }
    [Fact] public void Weekday_schedule_skips_weekend_and_does_not_schedule_at_end_boundary()
    {
        var options = new HealthToolOptions { Enabled = true, Start = new(8, 30), End = new(18, 0) };
        Assert.Equal(new DateTime(2026, 10, 5, 9, 30, 0), options.NextAfter(new(2026, 10, 2, 17, 30, 0)));
        Assert.Throws<ArgumentException>(() => (options with { Start = new(18, 0), End = new(8, 30) }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Days = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { IntervalMinutes = 0 }).Validate());
    }
    [Fact] public async Task Retention_prunes_events_older_than_seven_calendar_days()
    {
        await Enable(); await AdvanceTo(10, 20); await health.MarkDisplayedAsync([Screen.Pending!.Id], "old");
        clock.At = clock.At.AddDays(7); await health.TickAsync(); Assert.Empty(health.Snapshot.Events);
    }
    [Fact] public void Workdays_skip_holidays_include_makeup_days_and_cross_long_breaks()
    {
        Holiday[] holidays = [new(){Date=new(2026,10,3),Type="補班日"},new(){Date=new(2026,10,9),Type="國定假日"}];
        var options = new HealthToolOptions { Enabled=true, DayMode="Workdays" };
        Assert.Equal(new DateTime(2026,10,3,9,30,0),options.NextAfter(new(2026,10,2,17,30,0),holidays:holidays));
        Assert.False(options.IsActive(new(2026,10,9,10,0,0),holidays));
        Assert.True(options.IsActive(new(2026,10,3,10,0,0),holidays));
        Assert.False(options.IsActive(new(2026,10,4,10,0,0),holidays));
        Assert.True((options with{DayMode="Custom"}).IsActive(new(2026,10,9,10,0,0),holidays));
        Assert.False((options with{DayMode="Custom"}).IsActive(new(2026,10,3,10,0,0),holidays));
        Assert.True((options with{DayMode="Everyday"}).IsActive(new(2026,10,4,10,0,0),holidays));
        var longBreak=Enumerable.Range(0,14).Select(i=>new Holiday{Date=new DateOnly(2026,10,1).AddDays(i),Type="放假日"}).ToArray();
        Assert.Equal(new DateTime(2026,10,15,9,30,0),options.NextAfter(new(2026,10,1),holidays:longBreak));
        Assert.Throws<ArgumentException>(()=>(options with{DayMode="invalid"}).Validate());
    }
    [Theory]
    [InlineData("Water")][InlineData("Sitting")][InlineData("Screen")][InlineData("Stretch")]
    public async Task Workday_mode_persists_and_live_calendar_changes_suppress_pending_and_alignment(string kind)
    {
        var calendar=new Holidays();calendar.Rows=[new(){Date=new(2026,10,3),Type="補班日"}];
        var service=new HealthToolsService(settings,clock,calendar);
        clock.At=new(2026,10,2,17,50,0);
        await service.SaveToolAsync(new(){Kind=kind,Enabled=true,DayMode="Workdays"});
        Assert.Equal(new DateTime(2026,10,3,9,30,0),service.Snapshot.Tools.Single(t=>t.Options.Kind==kind).NextAt);
        var restarted=new HealthToolsService(settings,clock,calendar);await restarted.InitializeAsync();
        Assert.Equal("Workdays",restarted.Snapshot.Tools.Single(t=>t.Options.Kind==kind).Options.DayMode);
        clock.At=new(2026,10,3,9,29,50);await service.TickAsync();
        clock.At=clock.At.AddSeconds(10);await service.TickAsync();Assert.Single(service.Ready(new(),false,false));
        calendar.Rows=[new(){Date=new(2026,10,3),Type="放假日"}];clock.At=clock.At.AddMinutes(1);await service.TickAsync();
        Assert.Empty(service.Ready(new(),false,false));Assert.Null(service.Snapshot.Tools.Single(t=>t.Options.Kind==kind).Pending);
        Assert.Equal(new DateTime(2026,10,5,9,30,0),service.Snapshot.Tools.Single(t=>t.Options.Kind==kind).NextAt);
        await service.SavePreferencesAsync("AutoAlign","Right");await service.ObservePomodoroAsync(Focus);
        await service.ObservePomodoroAsync(new("Focus","AwaitingConfirmation"));Assert.Empty(service.Snapshot.AlignmentKinds);
        Assert.Empty(service.Ready(new(),false,false));
    }
    [Fact] public async Task Older_settings_keep_custom_weekdays_without_opt_in_to_workdays()
    {
        var options=HealthTools.Defaults().Select(o=>new HealthToolState(o with{Enabled=true})).ToArray();
        var json=System.Text.Json.JsonSerializer.Serialize(new HealthToolsState{Tools=options}).Replace(",\"DayMode\":\"Custom\"","");
        Assert.DoesNotContain("DayMode",json);await settings.SaveAsync(new(){Key=HealthToolsService.SettingsKey,Value=json});
        await health.InitializeAsync();Assert.All(health.Snapshot.Tools,t=>{Assert.Equal("Custom",t.Options.DayMode);Assert.Equal(62,t.Options.Days);});
    }
    private sealed class Holidays : IHolidayRepository
    {
        public IReadOnlyList<Holiday> Rows=[];
        public Task<IReadOnlyList<Holiday>> GetAllAsync(CancellationToken cancellationToken=default)=>Task.FromResult(Rows);
        public Task ReplaceCacheAsync(IReadOnlyList<Holiday> holidays,CancellationToken cancellationToken=default){Rows=holidays;return Task.CompletedTask;}
    }
    private sealed class Clock : TimeProvider
    {
        public DateTime At = new(2026, 9, 29, 10, 0, 0);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(At, TimeSpan.Zero);
    }
    private sealed class Settings : ISettingsRepository
    {
        private readonly Dictionary<string, Setting> values = [];
        public bool Fail;
        public Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(values.GetValueOrDefault(key));
        public Task SaveAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("disk unavailable");
            values[setting.Key] = setting; return Task.CompletedTask;
        }
    }
}
