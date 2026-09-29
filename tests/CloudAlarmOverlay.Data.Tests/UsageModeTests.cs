using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class UsageModeTests : IDisposable, IAppPaths
{
    public string DataDirectory { get; }=Path.Combine(Path.GetTempPath(),"CloudAlarmUsageTests",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private ServiceProvider services;
    private readonly Clock clock=new();
    public UsageModeTests(){services=Build();Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();}
    private ServiceProvider Build()=>new ServiceCollection().AddCore().AddData().AddSingleton<IAppPaths>(this).AddSingleton<TimeProvider>(clock).BuildServiceProvider();
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    [Fact] public async Task Personal_grants_persistent_admin_without_password_or_expiry_and_survives_restart()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("PERSONAL","本人",UsageModes.Personal);
        var session=Get<AdminSession>();Assert.True(session.IsPersonal);Assert.True(session.IsAuthenticated);
        clock.Advance(TimeSpan.FromDays(2));Assert.False(session.CheckExpiry());session.SignOut();Assert.True(session.IsAuthenticated);
        await Get<IAdminSettingsStore>().SaveAsync([new(){Key=LogRetentionPolicy.Key,Value="14"}]);
        Assert.False(await Get<IExitProtectionService>().IsRequiredAsync());
        services.Dispose();services=Build();await Get<IDeviceIdentityService>().InitializeAccessAsync();
        Assert.True(Get<AdminSession>().IsAuthenticated);Assert.True(Get<AdminSession>().IsPersonal);
        Assert.Equal("Personal",await Get<IDeviceIdentityService>().GetUsageModeAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<ISettingsRepository>().SaveAsync(new(){Key=UsageModes.Key,Value="Company"}));
        await Assert.ThrowsAsync<ArgumentException>(()=>Get<IAdminSettingsStore>().SaveAsync([new(){Key=UsageModes.Key,Value="Company"}]));
    }
    [Fact] public async Task Company_keeps_password_expiry_exit_protection_and_cannot_be_reinitialized_as_personal()
    {
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("WORK","公司",UsageModes.Company);
        await Get<IDeviceIdentityService>().InitializeAccessAsync();
        var session=Get<AdminSession>();Assert.False(session.IsAuthenticated);Assert.False(session.IsPersonal);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<IAdminSettingsStore>().SaveAsync([new(){Key=LogRetentionPolicy.Key,Value="14"}]));
        Assert.True(await Get<IExitProtectionService>().IsRequiredAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<IDeviceIdentityService>().SetInitialIdentityAsync("NEW","個人",UsageModes.Personal));
        Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("admin","12345"));
        clock.Advance(TimeSpan.FromMinutes(10));Assert.True(session.CheckExpiry());Assert.False(session.IsAuthenticated);
    }
    [Fact] public async Task Legacy_install_without_mode_stays_company_and_cannot_downgrade()
    {
        await Get<IDeviceRepository>().SaveLocalAsync(new(){DeviceId="LEGACY",DisplayName="既有安裝"});
        await Get<IDeviceIdentityService>().InitializeAccessAsync();
        Assert.Equal("Company",await Get<IDeviceIdentityService>().GetUsageModeAsync());Assert.False(Get<AdminSession>().IsAuthenticated);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<IDeviceRepository>().SaveInitialAsync(new(){DeviceId="OTHER"},UsageModes.Personal));
        Assert.Null(await Get<ISettingsRepository>().GetAsync(UsageModes.Key));
    }
    [Fact] public async Task Failed_initial_save_rolls_back_device_mode_and_access()
    {
        await Get<Database>().ExecuteAsync("CREATE TRIGGER RejectUsage BEFORE INSERT ON Settings WHEN NEW.Key='UsageMode' BEGIN SELECT RAISE(ABORT,'test write failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(()=>Get<IDeviceIdentityService>().SetInitialIdentityAsync("PC","本人",UsageModes.Personal));
        Assert.Null(await Get<IDeviceIdentityService>().GetLocalAsync());Assert.False(Get<AdminSession>().IsAuthenticated);
        Assert.Null(await Get<ISettingsRepository>().GetAsync(UsageModes.Key));
    }
    [Fact] public async Task Personal_exit_can_use_dedicated_password_and_backup_restore_preserves_local_mode()
    {
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("PC","本人",UsageModes.Personal);
        var exit=Get<IExitProtectionService>();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>exit.SetRequiredAsync(true));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<IAdminSettingsStore>().SaveAsync([new(){Key="ExitPasswordRequired",Value="true"}]));
        Assert.False(await exit.IsRequiredAsync());
        await exit.SetDedicatedPasswordAsync("test-only-123");await exit.SetRequiredAsync(true);
        Assert.True(await exit.VerifyDedicatedPasswordAsync("test-only-123"));Assert.False(await exit.VerifyDedicatedPasswordAsync("wrong"));
        var backup=Get<IBackupRestoreService>();var path=Path.Combine(DataDirectory,"test.calbak");
        await backup.CreateBackupAsync(path,new BackupCredentials("",""));
        await backup.RestoreAsync(path,true,new BackupCredentials("",""));
        Assert.True(Get<AdminSession>().IsPersonal);Assert.True(Get<AdminSession>().IsAuthenticated);
        Assert.Equal("Personal",await Get<IDeviceIdentityService>().GetUsageModeAsync());
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    private sealed class Clock:TimeProvider
    {
        private long stamp;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>stamp;
        public void Advance(TimeSpan duration)=>stamp+=duration.Ticks;
    }
}
