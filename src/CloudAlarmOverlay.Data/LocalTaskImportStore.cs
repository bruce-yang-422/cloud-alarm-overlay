using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Data;
public sealed class LocalTaskImportStore(Database db) : ILocalTaskImportStore
{
    public async Task<bool> InsertAsync(AlarmTask task,CancellationToken ct=default)
    {
        LocalTaskCsvService.Validate(task,DateTime.Now);
        return await db.ExecuteAsync("""
            INSERT OR IGNORE INTO Tasks(Id,Title,Description,Note,ScheduledAt,Source,Level,Enabled,IsTriggered,
                RequireAcknowledgement,Recurrence,SkipOnHoliday,CreatedAt,UpdatedAt)
            VALUES(@Id,@Title,@Description,@Note,@ScheduledAt,'本機',@Level,@Enabled,0,
                @RequireAcknowledgement,@Recurrence,@SkipOnHoliday,@CreatedAt,@UpdatedAt);
            """,task,ct)==1;
    }
}
