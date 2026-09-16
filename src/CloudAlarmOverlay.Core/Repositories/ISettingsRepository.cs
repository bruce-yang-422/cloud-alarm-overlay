using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ISettingsRepository
{
    Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SaveAsync(Setting setting, CancellationToken cancellationToken = default);
}

