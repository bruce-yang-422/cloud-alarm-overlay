using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Win32;
namespace CloudAlarmOverlay.Infrastructure.Services;
internal sealed class ThemeService : IThemeService, IDisposable
{
    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public ThemeColorStyle ColorStyle { get; private set; }
    public bool IsDark { get; private set; }
    public event Action<bool>? Changed;
    public ThemeService() { SystemEvents.UserPreferenceChanged += OnPreference; }
    public void Apply(ThemeMode mode, ThemeColorStyle colorStyle = ThemeColorStyle.Default)
    {
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        if(!Enum.IsDefined(colorStyle))throw new ArgumentOutOfRangeException(nameof(colorStyle));
        if(mode is ThemeMode.Pink or ThemeMode.Bamboo)
        { colorStyle=mode==ThemeMode.Pink?ThemeColorStyle.Pink:ThemeColorStyle.Bamboo; mode=ThemeMode.Light; }
        Mode=mode; ColorStyle=colorStyle;
        IsDark=mode==ThemeMode.Dark || mode==ThemeMode.System && ReadSystemDark();
        Changed?.Invoke(IsDark);
    }
    private static bool ReadSystemDark()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value && value==0; }
        catch(System.Security.SecurityException) { return false; }
    }
    private void OnPreference(object sender, UserPreferenceChangedEventArgs e) { if(Mode==ThemeMode.System)Apply(Mode,ColorStyle); }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreference;
}