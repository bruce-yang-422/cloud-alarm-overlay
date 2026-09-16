using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class PomodoroRepository(Database db) : IPomodoroRepository
{
    public Task<IReadOnlyList<PomodoroSetting>> GetSettingsAsync(CancellationToken cancellationToken=default)
        =>db.QueryAsync<PomodoroSetting>("SELECT * FROM PomodoroSettings",ct:cancellationToken);
    public async Task SaveSettingAsync(PomodoroSetting setting,CancellationToken cancellationToken=default)
        =>await db.ExecuteAsync("INSERT INTO PomodoroSettings(Key,Value) VALUES(@Key,@Value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value",setting,cancellationToken);
    public Task<IReadOnlyList<PomodoroLogEntry>> GetLogsAsync(DateTime from,DateTime to,CancellationToken cancellationToken=default)
        =>db.QueryAsync<PomodoroLogEntry>("SELECT * FROM PomodoroLog WHERE StartedAt>=@from AND StartedAt<@to ORDER BY StartedAt DESC",new{from,to},cancellationToken);
    public async Task SaveLogAsync(PomodoroLogEntry entry,CancellationToken cancellationToken=default)
        =>await db.ExecuteAsync("""
            INSERT INTO PomodoroLog(Id,Type,StartedAt,CompletedAt,Result,EndedAt,PlannedMinutes,IsActive)
            VALUES(@Id,@Type,@StartedAt,@CompletedAt,@Result,@EndedAt,@PlannedMinutes,@IsActive)
            ON CONFLICT(Id) DO UPDATE SET CompletedAt=excluded.CompletedAt,Result=excluded.Result,
            EndedAt=excluded.EndedAt,PlannedMinutes=excluded.PlannedMinutes,IsActive=excluded.IsActive
            """,entry,cancellationToken);
}