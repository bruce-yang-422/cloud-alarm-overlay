using System.Text.Json;
using Dapper;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class AdminSettingsStore(Database db,AdminSession session):IAdminSettingsStore
{
    public async Task SaveAsync(IReadOnlyList<Setting> settings,CancellationToken ct=default)
    {
        var actor=session.RequireAdmin();
        foreach(var setting in settings)
        {
            if(setting.Key is not ("UpdateManifestUrl" or "AllowUrgentSnooze" or "WeatherDefaultLocation" or "SyncOptions" or "FlashMilliseconds" or "QuietPeriods" or "SyncLinksLocked" or "ExitPasswordMode" or "ExitPasswordHash" or "ExitPasswordRequired"))
                throw new ArgumentException("此項目不屬於管理者可鎖定的設定。");
            if(setting.Key=="UpdateManifestUrl" && !string.IsNullOrWhiteSpace(setting.Value))UpdateCheckService.ValidateUrl(setting.Value);
            NotificationPreferences.Validate(setting);
            if(setting.Key=="SyncOptions")(JsonSerializer.Deserialize<SyncOptions>(setting.Value??"{}")??new()).Validate();
            if(setting.Key=="SyncLinksLocked" && setting.Value is not ("true" or "false"))throw new ArgumentException("連結鎖定值無效。");
            if(setting.Key=="ExitPasswordMode" && setting.Value is not ("Administrator" or "Dedicated"))throw new ArgumentException("結束驗證方式無效。");
            if(setting.Key=="ExitPasswordRequired" && setting.Value is not ("true" or "false"))throw new ArgumentException("結束程式密碼開關值無效。");
            if(setting.Key=="ExitPasswordHash" && setting.Value is not null)
            {
                var parts=setting.Value.Split(':');
                if(parts.Length!=4||parts[0]!="PBKDF2-SHA256"||!int.TryParse(parts[1],out var rounds)||rounds is <100_000 or >2_000_000||Convert.FromBase64String(parts[2]).Length!=32||Convert.FromBase64String(parts[3]).Length!=32)
                    throw new ArgumentException("結束程式密碼格式無效。");
            }
        }
        await using var c=await db.OpenAsync(ct);
        using var tx=c.BeginTransaction(deferred:false);
        foreach(var setting in settings)
        {
            session.RequireAdmin();
            if(setting.Key=="SyncOptions" && await c.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT Value FROM Settings WHERE Key='SyncLinksLocked';",transaction:tx,cancellationToken:ct))=="true")throw new InvalidOperationException("請先解除同步來源鎖定。");
            var old=await c.QuerySingleOrDefaultAsync<Setting>(new CommandDefinition("SELECT * FROM Settings WHERE Key=@Key;",setting,tx,cancellationToken:ct));
            if(old==setting)continue;
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO Settings(Key,Value,Locked) VALUES(@Key,@Value,@Locked) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value,Locked=excluded.Locked;",setting,tx,cancellationToken:ct));
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO AuditLogs(UserId,Action,OldValue,NewValue,CreatedAt) VALUES(@actor,@action,@oldValue,@newValue,@at);",
                new {actor,action=setting.Key,oldValue=setting.Key is "ExitPasswordHash" or "SyncOptions" or "UpdateManifestUrl"?(old?.Value is null?null:"已設定"):old is null?null:JsonSerializer.Serialize(old),newValue=setting.Key is "ExitPasswordHash" or "SyncOptions" or "UpdateManifestUrl"?(setting.Value is null?"已清除":"已設定"):JsonSerializer.Serialize(setting),at=DateTime.Now.ToString("O")},tx,cancellationToken:ct));
        }
        var exitMode=await c.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT Value FROM Settings WHERE Key='ExitPasswordMode';",transaction:tx,cancellationToken:ct));
        if(exitMode=="Dedicated" && string.IsNullOrWhiteSpace(await c.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT Value FROM Settings WHERE Key='ExitPasswordHash';",transaction:tx,cancellationToken:ct))))
            throw new InvalidOperationException("使用專用結束密碼前必須先設定密碼。");
        session.RequireAdmin();
        tx.Commit();
    }
}
