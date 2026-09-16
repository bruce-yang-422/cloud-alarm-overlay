using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Repositories;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IHolidayRepository
{
    Task<IReadOnlyList<Holiday>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ReplaceCacheAsync(IReadOnlyList<Holiday> holidays, CancellationToken cancellationToken = default);
}

