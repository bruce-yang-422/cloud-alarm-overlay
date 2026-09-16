using System.IO.Compression;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Data;
using Microsoft.Extensions.DependencyInjection;
namespace CloudAlarmOverlay.Data.Tests;

public sealed class MaintenanceTests : IDisposable
{
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmMaintenance",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
    private readonly Paths paths=new();
    private readonly ServiceProvider services;
    private T Get<T>() where T:notnull=>services.GetRequiredService<T>();
    public MaintenanceTests()
    {
        var s=new ServiceCollection();s.AddCore();s.AddData();s.AddSingleton<IAppPaths>(paths);services=s.BuildServiceProvider();
        Get<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
    }
    [Fact] public async Task Backup_roundtrip_preserves_unicode_markdown_and_conflicts_without_policy_changes()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("EMP-1","測試員");
        var task=new AlarmTask{Id="one",Title="訂單",Description="## 核帳\n\n- [x] ✅\n含,逗號與\"引號\"",ScheduledAt=DateTime.Now.AddHours(1),CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
        await Get<ITaskRepository>().SaveLocalAsync(task);
        await Get<ISettingsRepository>().SaveAsync(new(){Key="EmojiLibrary",Value="📦\n✅"});
        var file=Path.Combine(paths.DataDirectory,"test.calbak");
        await Get<IBackupRestoreService>().CreateBackupAsync(file);
        using(var zip=ZipFile.OpenRead(file)){Assert.Null(zip.GetEntry("admin.key"));Assert.NotNull(zip.GetEntry("manifest.json"));}
        Assert.Equal(1,(await Get<IBackupRestoreService>().InspectAsync(file)).Tasks);
        await Get<ITaskRepository>().SaveLocalAsync(task with{Title="本機較新"});
        var result=await Get<IBackupRestoreService>().RestoreAsync(file,false,null);
        Assert.Equal(1,result.SkippedTasks);Assert.Equal("本機較新",(await Get<ITaskRepository>().GetByIdAsync("one"))!.Title);
        await Get<ITaskRepository>().DeleteLocalAsync("one");
        result=await Get<IBackupRestoreService>().RestoreAsync(file,false,null);
        Assert.Equal(1,result.ImportedTasks);Assert.Equal(task.Description,(await Get<ITaskRepository>().GetByIdAsync("one"))!.Description);
    }
    [Fact] public async Task Corrupt_archive_fails_before_identity_or_tasks_are_changed()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("original","原使用者");
        var file=Path.Combine(paths.DataDirectory,"bad.calbak");
        using(var zip=ZipFile.Open(file,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("../bad").Open()))writer.Write("invalid");
        await Assert.ThrowsAsync<InvalidDataException>(()=>Get<IBackupRestoreService>().RestoreAsync(file));
        Assert.Equal("original",(await Get<IDeviceRepository>().GetLocalAsync())!.DeviceId);
    }
    [Fact] public async Task Restore_rolls_back_all_writes_and_preserves_locked_preferences()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("backup","原身分");
        await Get<ITaskRepository>().SaveLocalAsync(new(){Id="restore-task",Title="還原測試",ScheduledAt=DateTime.Now.AddDays(1),CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now});
        await Get<ISettingsRepository>().SaveAsync(new(){Key="FlashMilliseconds",Value="500"});
        var path=Path.Combine(paths.DataDirectory,"rollback.calbak");await Get<IBackupRestoreService>().CreateBackupAsync(path);
        var db=Get<Database>();
        await db.ExecuteAsync("UPDATE Devices SET DeviceId='current'; DELETE FROM Tasks; UPDATE Settings SET Value='1000',Locked=1 WHERE Key='FlashMilliseconds'; CREATE TRIGGER reject_restore BEFORE INSERT ON Tasks BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(()=>Get<IBackupRestoreService>().RestoreAsync(path));
        Assert.Equal("current",(await Get<IDeviceRepository>().GetLocalAsync())!.DeviceId);
        await db.ExecuteAsync("DROP TRIGGER reject_restore;");
        await Get<IBackupRestoreService>().RestoreAsync(path);
        Assert.Equal("1000",(await Get<ISettingsRepository>().GetAsync("FlashMilliseconds"))!.Value);
        Assert.True((await Get<ISettingsRepository>().GetAsync("FlashMilliseconds"))!.Locked);
    }
    [Fact] public async Task Backup_preserves_history_and_completed_occurrence_without_retrigger()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("EMP","測試");
        var task=new AlarmTask{Id="done",Title="已簽收",ScheduledAt=DateTime.Now.AddMinutes(-1),CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
        await Get<ITaskRepository>().SaveLocalAsync(task);
        var runtime=Get<IRuntimeStore>();
        await runtime.ClaimAsync("occurrence",task,task.ScheduledAt);
        await runtime.DisplayedAsync("occurrence",task,task.ScheduledAt,(await Get<IDeviceRepository>().GetLocalAsync())!);
        await runtime.CompleteAsync("occurrence");
        var path=Path.Combine(paths.DataDirectory,"history.calbak");await Get<IBackupRestoreService>().CreateBackupAsync(path);
        await Get<Database>().ExecuteAsync("DELETE FROM Tasks; DELETE FROM Occurrences; DELETE FROM AcknowledgementLogs;");
        var restored=await Get<IBackupRestoreService>().RestoreAsync(path,false,null);
        Assert.Equal(1,restored.ImportedHistory);
        Assert.False(await runtime.ClaimAsync("different-id",task,task.ScheduledAt));
        var logs=await Get<IAckLogRepository>().GetRangeAsync(DateTime.MinValue,DateTime.MaxValue);
        Assert.Equal("Acknowledged",Assert.Single(logs).Result);
    }
    [Fact] public async Task Password_backup_requires_credentials_and_password_change_preserves_account()
    {
        await Get<IDeviceIdentityService>().SetInitialIdentityAsync("EMP-1","測試員");
        var auth=Get<IAuthenticationService>();await auth.EnsureDefaultAdministratorAsync();
        var file=Path.Combine(paths.DataDirectory,"admin.calbak");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<IBackupRestoreService>().CreateBackupAsync(file,new BackupCredentials("admin","wrong")));
        await Get<IBackupRestoreService>().CreateBackupAsync(file,new BackupCredentials("admin","12345"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Get<IBackupRestoreService>().RestoreAsync(file));
        await auth.ChangePasswordAsync("admin","12345","Changed-Password!");
        Assert.False(await auth.AuthenticateAsync("admin","12345"));Assert.True(await auth.AuthenticateAsync("admin","Changed-Password!"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<IBackupRestoreService>().RestoreAsync(file,true,new BackupCredentials("admin","12345")));
        await Get<IBackupRestoreService>().RestoreAsync(file,true,new BackupCredentials("admin","Changed-Password!"));
        Assert.True(await auth.AuthenticateAsync("admin","12345"));
    }
    [Fact] public async Task Exit_password_can_be_reset_without_old_password_but_only_by_signed_in_admin()
    {
        var protection=Get<IExitProtectionService>();
        Assert.True(await protection.IsRequiredAsync());
        Assert.Equal("Administrator",await protection.GetModeAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>protection.SetDedicatedPasswordAsync("First-Exit-123"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>protection.SetRequiredAsync(false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<ISettingsRepository>().SaveAsync(new(){Key="ExitPasswordMode",Value="Dedicated"}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Get<ISettingsRepository>().SaveAsync(new(){Key="ExitPasswordRequired",Value="false"}));
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();
        Assert.True(await Get<IAuthenticationService>().AuthenticateAsync("admin","12345"));
        await protection.SetRequiredAsync(false);
        Assert.False(await protection.IsRequiredAsync());
        await protection.SetRequiredAsync(true);
        Assert.True(await protection.IsRequiredAsync());
        await Assert.ThrowsAsync<ArgumentException>(()=>protection.SetDedicatedPasswordAsync("short"));
        await protection.SetDedicatedPasswordAsync("First-Exit-123");
        Assert.Equal("Dedicated",await protection.GetModeAsync());
        Assert.True(await protection.VerifyDedicatedPasswordAsync("First-Exit-123"));
        Assert.False(await protection.VerifyDedicatedPasswordAsync("wrong"));
        var stored=(await Get<ISettingsRepository>().GetAsync("ExitPasswordHash"))!.Value!;
        Assert.DoesNotContain("First-Exit-123",stored);
        await protection.SetDedicatedPasswordAsync("Second-Exit-456");
        Assert.False(await protection.VerifyDedicatedPasswordAsync("First-Exit-123"));
        Assert.True(await protection.VerifyDedicatedPasswordAsync("Second-Exit-456"));
        var audit=await Get<IAuditLogRepository>().GetRangeAsync(DateTime.Today,DateTime.Now.AddSeconds(1));
        Assert.DoesNotContain(audit,a=>(a.OldValue??"").Contains(stored)||(a.NewValue??"").Contains(stored));
        await protection.UseAdministratorPasswordAsync();
        Assert.Equal("Administrator",await protection.GetModeAsync());
        Assert.Null((await Get<ISettingsRepository>().GetAsync("ExitPasswordHash"))!.Value);
        Assert.False(await protection.VerifyDedicatedPasswordAsync("Second-Exit-456"));
        Get<AdminSession>().SignOut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>protection.SetDedicatedPasswordAsync("Third-Exit-789"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>protection.SetRequiredAsync(false));
    }
    [Fact] public async Task Update_manifest_normalizes_versions_and_rejects_unsafe_downloads()
    {
        await Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();await Get<IAuthenticationService>().AuthenticateAsync("admin","12345");
        await Get<ISettingsRepository>().SaveAsync(new(){Key="UpdateManifestUrl",Value="https://example.test/version.json"});
        var handler=new ManifestHandler();using var client=new HttpClient(handler);
        var update=new UpdateCheckService(Get<ISettingsRepository>(),client);
        handler.Content="{\"latestVersion\":\"1.0.0\",\"downloadUrl\":\"https://example.test/setup\"}";
        Assert.Null(await update.CheckAsync());
        handler.Content="{\"latestVersion\":\"9.0.0\",\"downloadUrl\":\"https://example.test/setup\",\"releaseNote\":\"修正\"}";
        Assert.Equal(new Version(9,0,0),(await update.CheckAsync())!.LatestVersion);
        handler.Content="{\"latestVersion\":\"9.0.0\",\"downloadUrl\":\"file:///C:/setup.exe\"}";
        await Assert.ThrowsAsync<ArgumentException>(()=>update.CheckAsync());
        handler.Content=new string('x',66000);await Assert.ThrowsAsync<InvalidDataException>(()=>update.CheckAsync());
    }
    private sealed class ManifestHandler:HttpMessageHandler
    {
        public string Content="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(Content)});
    }
    public void Dispose(){services.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(paths.DataDirectory,true);}
}
