using System.IO;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Win32;

namespace CloudAlarmOverlay.App.Services;

public sealed class AutoStartService(AdminSession session):IAutoStartService
{
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string PreferenceKey=@"Software\CloudAlarmOverlay";
    private const string ValueName="CloudAlarmOverlay";
    private const string PreferenceName="AutoStartEnabled";

    private static string ExecutablePath=>Path.Combine(AppContext.BaseDirectory,"CloudAlarmOverlay.App.exe");

    public bool IsEnabled()
    {
        using var key=Registry.CurrentUser.OpenSubKey(RunKey);
        var value=key?.GetValue(ValueName) as string;
        return string.Equals(value,$"\"{ExecutablePath}\"",StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        session.RequireAdmin();
        if(enabled && !File.Exists(ExecutablePath))
            throw new FileNotFoundException("找不到程式執行檔，無法設定登入自動啟動。",ExecutablePath);

        using var run=Registry.CurrentUser.CreateSubKey(RunKey,true)
            ??throw new InvalidOperationException("無法開啟 Windows 登入啟動項目。");
        if(enabled)run.SetValue(ValueName,$"\"{ExecutablePath}\"",RegistryValueKind.String);
        else run.DeleteValue(ValueName,false);

        using var preference=Registry.CurrentUser.CreateSubKey(PreferenceKey,true)
            ??throw new InvalidOperationException("無法儲存自動啟動設定。");
        preference.SetValue(PreferenceName,enabled?1:0,RegistryValueKind.DWord);
    }
}
