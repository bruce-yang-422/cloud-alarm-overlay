using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IThemeService
{
    void Apply(ThemeMode mode, ThemeColorStyle colorStyle = ThemeColorStyle.Default);
    ThemeMode Mode { get; }
    ThemeColorStyle ColorStyle { get; }
    bool IsDark { get; }
    event Action<bool>? Changed;
}

