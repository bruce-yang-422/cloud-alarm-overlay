using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ILunarCalendarRepository
{
    Task<IReadOnlyList<LunarCalendarEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<LunarCalendarEntry?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task ReplaceCacheAsync(IReadOnlyList<LunarCalendarEntry> entries, CancellationToken cancellationToken = default);
}
