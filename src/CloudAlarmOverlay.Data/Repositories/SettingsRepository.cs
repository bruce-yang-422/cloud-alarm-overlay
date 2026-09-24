using Dapper;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class SettingsRepository(Database db,AdminSession session):ISettingsRepository
{
    public async Task<Setting?> GetAsync(string key,CancellationToken cancellationToken=default)=>
        (await db.QueryAsync<Setting>("SELECT * FROM Settings WHERE Key=@key;",new {key},cancellationToken)).SingleOrDefault();
    public async Task SaveAsync(Setting setting,CancellationToken cancellationToken=default)
    {
        if(setting.Key is "AllowUrgentSnooze" or "WeatherDefaultLocation" or "SyncOptions" or "SyncLinksLocked" or "ExitPasswordMode" or "ExitPasswordHash" or "ExitPasswordRequired")throw new UnauthorizedAccessException("此項目只能透過管理者設定修改。");
        if(setting.Key=="UpdateManifestUrl"){session.RequireAdmin();if(!string.IsNullOrWhiteSpace(setting.Value))UpdateCheckService.ValidateUrl(setting.Value);}
        if(setting.Locked)throw new UnauthorizedAccessException("一般設定寫入不能變更鎖定狀態。");
        NotificationPreferences.Validate(setting);
        await using var c=await db.OpenAsync(cancellationToken);
        using var tx=c.BeginTransaction(deferred:false);
        if(setting.Key==HomePinOptions.SettingKey)
        {
            var count=await c.ExecuteScalarAsync<int>(new CommandDefinition(HomePinStorage.CountSql,transaction:tx,cancellationToken:cancellationToken));
            if(count>HomePinOptions.ReadLimit(setting.Value))
                throw new InvalidOperationException($"目前已釘選 {count} 項，請先取消部分釘選，再降低上限。");
        }
        var old=await c.QuerySingleOrDefaultAsync<Setting>(new CommandDefinition("SELECT * FROM Settings WHERE Key=@Key;",setting,tx,cancellationToken:cancellationToken));
        if(old?.Locked==true)throw new UnauthorizedAccessException("此設定已由管理者鎖定。");
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO Settings(Key,Value,Locked) VALUES(@Key,@Value,0) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;",setting,tx,cancellationToken:cancellationToken));
        if(old?.Value!=setting.Value && session.IsAuthenticated && (setting.Key is "FlashMilliseconds" or "QuietPeriods" or "UpdateManifestUrl" || setting.Key.StartsWith("Sound:",StringComparison.Ordinal)))
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO AuditLogs(UserId,Action,OldValue,NewValue,CreatedAt) VALUES(@actor,@Key,@oldValue,@Value,@at);",
                new {actor=session.RequireAdmin(),setting.Key,oldValue=old?.Value,setting.Value,at=DateTime.Now.ToString("O")},tx,cancellationToken:cancellationToken));
        tx.Commit();
    }
}
