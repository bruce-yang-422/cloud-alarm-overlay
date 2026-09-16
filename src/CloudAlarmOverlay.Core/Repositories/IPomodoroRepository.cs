using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IPomodoroRepository
{
    Task<IReadOnlyList<PomodoroSetting>> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingAsync(PomodoroSetting setting, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PomodoroLogEntry>> GetLogsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task SaveLogAsync(PomodoroLogEntry entry, CancellationToken cancellationToken = default);
}

