using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.Services;
namespace CloudAlarmOverlay.App.ViewModels;

public partial class MaintenanceViewModel(IBackupRestoreService backups, IUpdateCheckService updates, ISettingsRepository settings, IThemeService theme, AdminSession session, IBrowserLauncher browser) : ObservableObject
{
    public void RequireAdministrator()=>session.RequireAdmin();
    public IBackupRestoreService Backups {get;}=backups;
    public bool CanEditUpdateUrl=>session.IsAuthenticated;
    public async Task AdminSessionChangedAsync()
    {
        OnPropertyChanged(nameof(CanEditUpdateUrl));
        SaveUpdateUrlCommand.NotifyCanExecuteChanged();
        UpdateUrl=(await settings.GetAsync("UpdateManifestUrl"))?.Value??"";
    }
    public string VersionLabel => "目前版本 "+UpdateCheckService.CurrentVersion.ToString(3);
    [ObservableProperty] private string message="";
    [ObservableProperty] private string updateUrl="";
    [ObservableProperty] private string themeChoice="跟隨系統";
    [ObservableProperty] private string themeColorChoice="預設";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool keepWindowAspectRatio=true;
    partial void OnKeepWindowAspectRatioChanged(bool value)
    {
        if(!loading)_=SaveWindowBehaviorAsync(value);
    }
    private async Task SaveWindowBehaviorAsync(bool value)
    {
        try {await settings.SaveAsync(new(){Key="KeepWindowAspectRatio",Value=value.ToString()});}
        catch(Exception ex){Message="視窗設定儲存失敗："+ex.Message;}
    }
    public string[] Themes {get;}=["淺色","暗色","跟隨系統"];
    public string[] ThemeColors {get;}=["預設","櫻花粉","若竹綠","薰衣草紫","夕陽橘","極簡銀白"];
    public UpdateInfo? AvailableUpdate {get; private set;}
    private bool loading;
    public async Task InitializeAsync()
    {
        loading=true;
        try
        {
            UpdateUrl=(await settings.GetAsync("UpdateManifestUrl"))?.Value??"";
            var savedTheme=(await settings.GetAsync("ThemeMode"))?.Value;
            ThemeChoice=savedTheme switch{"粉紅色" or "若竹色" or "淺粉色" or "淺若竹色"=>"淺色","深色" or "暗粉色" or "暗若竹色"=>"暗色",null=>"跟隨系統",_=>savedTheme};
            var savedColor=(await settings.GetAsync("ThemeColorStyle"))?.Value;
            ThemeColorChoice=savedTheme switch
            {"粉紅色" or "淺粉色" or "暗粉色"=>"櫻花粉","若竹色" or "淺若竹色" or "暗若竹色"=>"若竹綠",_=>savedColor switch {"粉紅色"=>"櫻花粉","若竹色"=>"若竹綠",_=>savedColor??"預設"}};
            KeepWindowAspectRatio=!bool.TryParse((await settings.GetAsync("KeepWindowAspectRatio"))?.Value,out var keepRatio)||keepRatio;
            theme.Changed-=ApplyPalette; theme.Changed+=ApplyPalette;
            ApplyTheme();
        }
        finally {loading=false;}
    }
    private void ApplyPalette(bool dark)=>AdaptiveBrushExtension.Apply(dark,theme.ColorStyle);
    private void ApplyTheme()=>theme.Apply(ThemeChoice switch{"暗色" or "深色"=>ThemeMode.Dark,"淺色"=>ThemeMode.Light,_=>ThemeMode.System},
        ThemeColorChoice switch{"櫻花粉" or "粉紅色"=>ThemeColorStyle.Pink,"若竹綠" or "若竹色"=>ThemeColorStyle.Bamboo,"薰衣草紫"=>ThemeColorStyle.Lavender,"夕陽橘"=>ThemeColorStyle.Sunset,"極簡銀白"=>ThemeColorStyle.Silver,_=>ThemeColorStyle.Default});
    partial void OnThemeChoiceChanged(string value)
    {
        if(!loading) _=SaveThemeAsync();
    }
    partial void OnThemeColorChoiceChanged(string value)
    {
        if(!loading) _=SaveThemeAsync();
    }
    private async Task SaveThemeAsync()
    {
        try
        {
            // Save the pair in order even when the user clicks both selectors quickly.
            await themeSaveGate.WaitAsync();
            try
            {
                var mode=ThemeChoice; var color=ThemeColorChoice;
                await settings.SaveAsync(new(){Key="ThemeColorStyle",Value=color});
                await settings.SaveAsync(new(){Key="ThemeMode",Value=mode});
                ApplyTheme();
            }
            finally {themeSaveGate.Release();}
        }
        catch(Exception ex){Message=ex.Message;}
    }
    private readonly SemaphoreSlim themeSaveGate=new(1,1);
    [RelayCommand(CanExecute=nameof(CanEditUpdateUrl))] private async Task SaveUpdateUrlAsync()
    {
        try {session.RequireAdmin(); if(!string.IsNullOrWhiteSpace(UpdateUrl))UpdateCheckService.ValidateUrl(UpdateUrl); await settings.SaveAsync(new(){Key="UpdateManifestUrl",Value=UpdateUrl.Trim()}); Message="更新來源已儲存。";}
        catch(Exception ex){Message=ex.Message;}
    }
    [RelayCommand] public async Task CheckUpdateAsync()
    {
        Busy=true; AvailableUpdate=null; Message="正在檢查更新…";
        try { AvailableUpdate=await updates.CheckAsync(); Message=AvailableUpdate is {} info?$"有新版本 {info.LatestVersion}：{info.ReleaseNote}":"目前已是最新版本。"; }
        catch(Exception ex){Message="無法檢查更新："+ex.Message;}
        finally {Busy=false; OnPropertyChanged(nameof(AvailableUpdate));}
    }
    [RelayCommand] private async Task OpenDownloadAsync()
    {
        Busy=true; Message="正在取得下載連結…";
        try
        {
            var update=await updates.GetManifestAsync();
            browser.Open(update.DownloadUrl);
            Message="已交由瀏覽器下載安裝檔。";
        }
        catch(Exception ex){Message="無法啟動安裝檔下載："+ex.Message;}
        finally {Busy=false;}
    }
}
