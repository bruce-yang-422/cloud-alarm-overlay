using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Win32;
namespace CloudAlarmOverlay.Infrastructure.Services;
internal sealed class ThemeService : IThemeService, IDisposable
{
    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public bool IsDark { get; private set; }
    public event Action<bool>? Changed;
    public ThemeService() { SystemEvents.UserPreferenceChanged += OnPreference; }
    public void Apply(ThemeMode mode)
    {
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        Mode=mode;
        IsDark=mode==ThemeMode.Dark || mode==ThemeMode.System && ReadSystemDark();
        Changed?.Invoke(IsDark);
    }
    private static bool ReadSystemDark()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value && value==0; }
        catch(System.Security.SecurityException) { return false; }
    }
    private void OnPreference(object sender, UserPreferenceChangedEventArgs e) { if(Mode==ThemeMode.System)Apply(Mode); }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreference;
}