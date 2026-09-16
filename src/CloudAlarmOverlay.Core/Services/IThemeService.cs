using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IThemeService
{
    void Apply(ThemeMode mode);
    ThemeMode Mode { get; }
    bool IsDark { get; }
    event Action<bool>? Changed;
}

