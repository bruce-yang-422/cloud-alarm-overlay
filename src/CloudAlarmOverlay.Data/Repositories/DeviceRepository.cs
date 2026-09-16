using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class DeviceRepository(Database db) : IDeviceRepository
{
    public async Task<Device?> GetLocalAsync(CancellationToken cancellationToken=default)
        => (await db.QueryAsync<Device>("SELECT * FROM Devices LIMIT 1;",ct:cancellationToken)).SingleOrDefault();
    public Task SaveLocalAsync(Device device, CancellationToken cancellationToken=default)
        => db.ExecuteAsync("INSERT INTO Devices(Id,DeviceId,DisplayName,LastSeen,Version) SELECT 1,@DeviceId,@DisplayName,@LastSeen,@Version WHERE NOT EXISTS(SELECT 1 FROM Devices);",device,cancellationToken);
}
