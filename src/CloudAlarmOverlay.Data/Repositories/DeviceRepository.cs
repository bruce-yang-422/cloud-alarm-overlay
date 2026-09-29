using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class DeviceRepository(Database db) : IDeviceRepository
{
    public async Task<Device?> GetLocalAsync(CancellationToken cancellationToken=default)
        => (await db.QueryAsync<Device>("SELECT * FROM Devices LIMIT 1;",ct:cancellationToken)).SingleOrDefault();
    public Task SaveLocalAsync(Device device, CancellationToken cancellationToken=default)
        => db.ExecuteAsync("INSERT INTO Devices(Id,DeviceId,DisplayName,LastSeen,Version) SELECT 1,@DeviceId,@DisplayName,@LastSeen,@Version WHERE NOT EXISTS(SELECT 1 FROM Devices);",device,cancellationToken);
    public async Task SaveInitialAsync(Device device, string usageMode, CancellationToken cancellationToken=default)
    {
        UsageModes.Validate(usageMode);
        await using var c=await db.OpenAsync(cancellationToken);
        using var tx=c.BeginTransaction(deferred:false);
        if(await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT (SELECT COUNT(*) FROM Devices)+(SELECT COUNT(*) FROM Settings WHERE Key='UsageMode');",transaction:tx,cancellationToken:cancellationToken))>0)
            throw new InvalidOperationException("使用方式與裝置身分已設定，無法重新執行首次設定。");
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO Devices(Id,DeviceId,DisplayName,LastSeen,Version) VALUES(1,@DeviceId,@DisplayName,@LastSeen,@Version);",device,tx,cancellationToken:cancellationToken));
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO Settings(Key,Value,Locked) VALUES('UsageMode',@usageMode,1);",new {usageMode},tx,cancellationToken:cancellationToken));
        if(usageMode==UsageModes.Personal)
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO Settings(Key,Value,Locked) VALUES('ExitPasswordRequired','false',0) ON CONFLICT(Key) DO UPDATE SET Value='false';",transaction:tx,cancellationToken:cancellationToken));
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO AuditLogs(UserId,Action,NewValue,CreatedAt) VALUES('首次設定','選擇使用方式',@usageMode,@at);",new{usageMode,at=DateTime.Now.ToString("O")},tx,cancellationToken:cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();tx.Commit();
    }
}
