using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
namespace CloudAlarmOverlay.Data.Tests;
public sealed class MilestoneTwoTests:IDisposable
{
    private readonly Paths paths=new();
    private readonly TestClock clock=new();
    private readonly Capture presenter=new();
    private readonly ServiceProvider services;
    public MilestoneTwoTests()
    {
        var c=new ServiceCollection().AddCore().AddData();
        c.AddSingleton<IAppPaths>(paths);c.AddSingleton<TimeProvider>(clock);c.AddSingleton<IAlarmPresenter>(presenter);
        services=c.BuildServiceProvider();
        Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    [Fact] public async Task Default_admin_is_hashed_idempotent_and_does_not_replace_existing_accounts()
    {
        var auth=Get<IAuthenticationService>();
        await auth.EnsureDefaultAdministratorAsync();
        var original=(await Get<IUserRepository>().GetByUsernameAsync("admin"))!;
        Assert.NotEqual("12345",original.PasswordHash);
        Assert.True(await auth.AuthenticateAsync("admin","12345"));
        Assert.False(await auth.AuthenticateAsync("admin","wrong"));
        await auth.EnsureDefaultAdministratorAsync();
        Assert.Equal(original,(await Get<IUserRepository>().GetByUsernameAsync("admin"))!);
        await Get<IUserRepository>().SaveAsync(original with {Enabled=false});
        await auth.EnsureDefaultAdministratorAsync();
        Assert.False(await auth.AuthenticateAsync("admin","12345"));
    }
    [Fact] public async Task Default_admin_does_not_add_an_alternate_login_to_configured_installations()
    {
        await Login();
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();
        Assert.Null(await Get<IUserRepository>().GetByUsernameAsync("admin"));
        Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("it-admin","Testing-1234"));
    }
    private async Task Login()
    {
        await Get<IAuthenticationService>().CreateInitialAsync("it-admin","Testing-1234");
        Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("it-admin","Testing-1234"));
    }
    [Fact] public async Task Initial_account_is_atomic_hashed_and_never_replaced()
    {
        var auth=Get<IAuthenticationService>();
        Assert.False(await auth.HasAdministratorAsync());
        await auth.CreateInitialAsync("it-admin","Testing-1234");
        var user=(await Get<IUserRepository>().GetByUsernameAsync("it-admin"))!;
        Assert.DoesNotContain("Testing-1234",user.PasswordHash);
        Assert.Equal(32,Convert.FromBase64String(user.Salt).Length);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>auth.CreateInitialAsync("second","Another-1234"));
        Assert.False(await auth.AuthenticateAsync("it-admin","wrong"));
        Assert.False(await auth.AuthenticateAsync("unknown","Testing-1234"));
        Assert.False(Get<AdminSession>().IsAuthenticated);
        Assert.True(await auth.AuthenticateAsync("it-admin","Testing-1234"));
        var audit=await Get<IAuditLogRepository>().GetRangeAsync(DateTime.Today,DateTime.Now.AddSeconds(1));
        Assert.Contains(audit,a=>a.Action=="管理者登入");
        Assert.DoesNotContain(audit,a=>(a.OldValue??"").Contains("Testing-1234")||(a.NewValue??"").Contains("Testing-1234"));
        await Get<IUserRepository>().SaveAsync(user with{Enabled=false});
        Get<AdminSession>().SignOut();
        Assert.False(await auth.AuthenticateAsync("it-admin","Testing-1234"));
    }
    [Fact] public async Task Concurrent_first_account_creation_has_exactly_one_winner()
    {
        async Task<bool> TryCreate(string name)
        {
            try{await Get<IAuthenticationService>().CreateInitialAsync(name,"Testing-1234");return true;}
            catch(InvalidOperationException){return false;}
        }
        var results=await Task.WhenAll(TryCreate("first"),TryCreate("second"));
        Assert.Single(results,x=>x);
    }
    [Fact] public async Task Idle_timeout_is_monotonic_activity_extends_it_and_expired_session_cannot_be_revived()
    {
        await Login();
        var session=Get<AdminSession>();
        clock.Advance(TimeSpan.FromMinutes(14));Assert.True(session.IsAuthenticated);
        session.Touch();clock.Advance(TimeSpan.FromMinutes(14));Assert.True(session.IsAuthenticated);
        clock.Advance(TimeSpan.FromMinutes(1));Assert.True(session.CheckExpiry());
        session.Touch();Assert.False(session.IsAuthenticated);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<IAdminSettingsStore>().SaveAsync([new(){Key="FlashMilliseconds",Value="500"}]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<IAuditLogRepository>().GetRangeAsync(DateTime.MinValue,DateTime.MaxValue));
    }
    [Fact] public async Task Locks_are_enforced_below_UI_sounds_cannot_be_locked_and_admin_changes_are_audited()
    {
        var store=Get<IAdminSettingsStore>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveAsync([new(){Key="FlashMilliseconds",Value="800",Locked=true}]));
        await Login();
        await store.SaveAsync([new(){Key="FlashMilliseconds",Value="800",Locked=true},new(){Key="QuietPeriods",Value="[]",Locked=true}]);
        Get<AdminSession>().SignOut();
        var repo=Get<ISettingsRepository>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repo.SaveAsync(new(){Key="FlashMilliseconds",Value="500"}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repo.SaveAsync(new(){Key="QuietPeriods",Value="[]"}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repo.SaveAsync(new(){Key="Sound:強制通知",Value="{}",Locked=true}));
        await repo.SaveAsync(new(){Key="Sound:強制通知",Value="{\"Enabled\":true}"});
        Assert.True((await Get<NotificationPreferences>().SoundAsync(AlarmLevels.Max)).Enabled);
        Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("it-admin","Testing-1234"));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.SaveAsync([new(){Key="Sound:強制通知",Value="{}",Locked=true}]));
        var entries=await Get<IAuditLogRepository>().GetRangeAsync(DateTime.MinValue,DateTime.MaxValue);
        Assert.Contains(entries,e=>e.Action=="FlashMilliseconds"&&e.NewValue!.Contains("800"));
        await store.SaveAsync([new(){Key="FlashMilliseconds",Value="800",Locked=false}]);
        Get<AdminSession>().SignOut();
        await repo.SaveAsync(new(){Key="FlashMilliseconds",Value="1000"});
        Assert.Equal(1000,await Get<NotificationPreferences>().FlashMillisecondsAsync());
    }
    [Fact] public async Task Audit_failure_rolls_back_settings_and_lock_together()
    {
        await Login();
        await Get<IAdminSettingsStore>().SaveAsync([new(){Key="FlashMilliseconds",Value="500"}]);
        await Get<Database>().ExecuteAsync("CREATE TRIGGER reject_audit BEFORE INSERT ON AuditLogs BEGIN SELECT RAISE(ABORT,'simulated disk failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(()=>Get<IAdminSettingsStore>().SaveAsync([new(){Key="FlashMilliseconds",Value="800",Locked=true}]));
        var setting=await Get<ISettingsRepository>().GetAsync("FlashMilliseconds");
        Assert.Equal("500",setting!.Value);Assert.False(setting.Locked);
    }
    [Fact] public async Task Sync_source_requires_admin_and_link_unlock_and_writes_audit()
    {
        var config=Get<SyncConfiguration>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>config.SaveAsync(new()));
        await Login();
        await config.SaveAsync(new(){SheetBId="B",TasksBGid="5"});
        await Get<IAdminSettingsStore>().SaveAsync([new(){Key="SyncLinksLocked",Value="true"}]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>config.SaveAsync(new()));
        Assert.Equal("B",(await config.LoadAsync()).SheetBId);
        Get<AdminSession>().SignOut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<ISettingsRepository>().SaveAsync(new(){Key="SyncOptions",Value="{}"}));
    }
    [Theory]
    [InlineData("緊急提醒","FALSE","緊急提醒",false)]
    [InlineData("重要提醒","TRUE","重要提醒",true)]
    [InlineData("-",null,"強制通知",true)]
    [InlineData(null,"-","強制通知",true)]
    public void Employee_policy_caps_level_and_applies_ack_override(string? max,string? ack,string expected,bool require)
    {
        var task=SampleTask() with{Level=AlarmLevels.Max,RequireAcknowledgement=true};
        var result=Get<IEmployeeLevelPolicyService>().GetPolicy(task,new(){DeviceId="PC",DisplayName="Amy"},[new(){DeviceId="PC",Name="Amy",MaxAllowedLevel=max,RequireAckOverride=ack}]);
        Assert.Equal(expected,result.Level);Assert.Equal(require,result.RequireAcknowledgement);
    }
    [Fact] public void Name_has_priority_blank_names_do_not_match_and_ceiling_never_upgrades()
    {
        var policy=Get<IEmployeeLevelPolicyService>();
        Employee[] employees=[new(){DeviceId="PC",MaxAllowedLevel=AlarmLevels.Mid},new(){DeviceId="other",Name="Amy",MaxAllowedLevel=AlarmLevels.High}];
        Assert.Equal(AlarmLevels.High,policy.GetPolicy(SampleTask(),new(){DeviceId="PC",DisplayName="Amy"},employees).Level);
        Assert.Equal(AlarmLevels.Mid,policy.GetPolicy(SampleTask(),new(){DeviceId="PC",DisplayName=""},employees).Level);
        Assert.Equal(AlarmLevels.Low,policy.GetPolicy(SampleTask() with{Level=AlarmLevels.Low},new(){DeviceId="PC"},employees).Level);
        Assert.Equal(AlarmLevels.Max,policy.GetPolicy(SampleTask(),new(){DeviceId="unknown"},employees).Level);
    }
    [Fact] public async Task Real_alarm_dispatch_passes_effective_policy_without_mutating_task_template()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("PC","Amy");
        await Get<IEmployeeRepository>().ReplaceCacheAsync([new(){DeviceId="PC",Name="Amy",MaxAllowedLevel=AlarmLevels.High,RequireAckOverride="FALSE"}]);
        var task=SampleTask();
        await Get<ITaskRepository>().ReplaceCloudCacheAsync(TaskSources.SheetA,[task]);
        await Get<IAlarmService>().EnqueueAsync(task);
        Assert.Equal(AlarmLevels.High,presenter.Last!.Level);Assert.False(presenter.Last.RequireAcknowledgement);
        Assert.Equal(AlarmLevels.Max,(await Get<ITaskRepository>().GetByIdAsync(task.Id))!.Level);
    }
    [Fact] public async Task Quiet_periods_support_midnight_boundary_and_never_delay_maximum()
    {
        await Get<ISettingsRepository>().SaveAsync(new(){Key="QuietPeriods",Value="[{\"Start\":\"23:00\",\"End\":\"07:00\"}]"});
        var prefs=Get<NotificationPreferences>();
        Assert.True(await prefs.IsQuietAsync(AlarmLevels.High,new(2026,9,15,23,0,0)));
        Assert.True(await prefs.IsQuietAsync(AlarmLevels.Mid,new(2026,9,16,6,59,0)));
        Assert.False(await prefs.IsQuietAsync(AlarmLevels.Mid,new(2026,9,16,7,0,0)));
        Assert.False(await prefs.IsQuietAsync(AlarmLevels.Max,new(2026,9,16,1,0,0)));
        await Assert.ThrowsAsync<ArgumentException>(()=>Get<ISettingsRepository>().SaveAsync(new(){Key="QuietPeriods",Value="[{\"Start\":\"12:00\",\"End\":\"12:00\"}]"}));
    }
    [Fact] public async Task Audit_range_is_inclusive_and_system_events_are_persisted()
    {
        await Login();
        await Get<IAuditService>().RecordAsync(new(){UserId="it-admin",Action="測試",CreatedAt=new(2026,1,1,23,59,59),OldValue="a,\"b\"\r\nc",NewValue="變更"});
        var row=Assert.Single(await Get<IAuditLogRepository>().GetRangeAsync(new(2026,1,1),new(2026,1,1,23,59,59)));
        Assert.Equal("變更",row.NewValue);
        Assert.NotEmpty(await Get<ISystemEventStore>().GetRangeAsync(DateTime.Today,DateTime.Now.AddSeconds(1)));
    }
    [Fact] public async Task V2_upgrade_preserves_admin_hash_settings_locks_and_audit()
    {
        var prior=new Paths();
        try
        {
            var factory=new SqliteConnectionFactory(prior);
            await using(var c=await factory.OpenConnectionAsync())
            {
                foreach(var version in new[]{"V1","V2"})
                {
                    using var stream=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations."+version+".sql")!;
                    using var reader=new StreamReader(stream);using var command=c.CreateCommand();
                    command.CommandText=await reader.ReadToEndAsync();await command.ExecuteNonQueryAsync();
                }
                using var seed=c.CreateCommand();
                seed.CommandText="INSERT INTO Users(Username,PasswordHash,Salt) VALUES('legacy','keep-hash','keep-salt'); INSERT INTO Settings(Key,Value,Locked) VALUES('FlashMilliseconds','800',1); INSERT INTO AuditLogs(UserId,Action,CreatedAt) VALUES('legacy','before-migration','2026-01-01T00:00:00'); PRAGMA user_version=2;";
                await seed.ExecuteNonQueryAsync();
            }
            await new DatabaseInitializer(factory).InitializeAsync();
            var db=new Database(factory);
            Assert.Equal(5,Assert.Single(await db.QueryAsync<int>("PRAGMA user_version;")));
            Assert.Equal("keep-hash",Assert.Single(await db.QueryAsync<string>("SELECT PasswordHash FROM Users;")));
            Assert.Equal(1,Assert.Single(await db.QueryAsync<int>("SELECT Locked FROM Settings WHERE Key='FlashMilliseconds';")));
            Assert.Equal("before-migration",Assert.Single(await db.QueryAsync<string>("SELECT Action FROM AuditLogs;")));
        }
        finally{if(Directory.Exists(prior.DataDirectory))Directory.Delete(prior.DataDirectory,true);}
    }

    private static AlarmTask SampleTask()=>new(){Id="SheetA:policy",ExternalId="policy",Source=TaskSources.SheetA,Title="政策測試",Level=AlarmLevels.Max,RequireAcknowledgement=true,ScheduledAt=DateTime.Now,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
    public void Dispose(){services.Dispose();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmOverlay.M2.Tests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
    private sealed class TestClock:TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>timestamp;
        public void Advance(TimeSpan elapsed)=>timestamp+=elapsed.Ticks;
    }
    private sealed class Capture:IAlarmPresenter
    {
        public AlarmTask? Last;
        public Task ShowAsync(string occurrenceId,AlarmTask task,DateTime at,bool preview,CancellationToken ct=default){Last=task;return System.Threading.Tasks.Task.CompletedTask;}
    }
}
