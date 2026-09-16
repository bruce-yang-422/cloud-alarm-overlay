using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ISoundService
{
    IReadOnlyList<string> GetAvailableSounds();
    Task PlayAsync(string soundName, CancellationToken cancellationToken = default);
}

