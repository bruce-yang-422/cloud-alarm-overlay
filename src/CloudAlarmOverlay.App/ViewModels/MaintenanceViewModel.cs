using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.App.Styles;
namespace CloudAlarmOverlay.App.ViewModels;

public partial class MaintenanceViewModel(IBackupRestoreService backups, IUpdateCheckService updates, ISettingsRepository settings, IThemeService theme, AdminSession session,IAuthenticationService authentication) : ObservableObject
{
    public void RequireAdministrator()=>session.RequireAdmin();
    public Task ChangePasswordAsync(string username,string current,string password)=>authentication.ChangePasswordAsync(username,current,password);
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
    public string[] Themes {get;}=["淺色","深色","跟隨系統"];
    public UpdateInfo? AvailableUpdate {get; private set;}
    private bool loading;
    public async Task InitializeAsync()
    {
        loading=true;
        try
        {
            UpdateUrl=(await settings.GetAsync("UpdateManifestUrl"))?.Value??"";
            ThemeChoice=(await settings.GetAsync("ThemeMode"))?.Value??"跟隨系統";
            KeepWindowAspectRatio=!bool.TryParse((await settings.GetAsync("KeepWindowAspectRatio"))?.Value,out var keepRatio)||keepRatio;
            theme.Changed-=AdaptiveBrushExtension.Apply; theme.Changed+=AdaptiveBrushExtension.Apply;
            ApplyTheme();
        }
        finally {loading=false;}
    }
    private void ApplyTheme()=>theme.Apply(ThemeChoice switch{"深色"=>ThemeMode.Dark,"淺色"=>ThemeMode.Light,_=>ThemeMode.System});
    partial void OnThemeChoiceChanged(string value)
    {
        if(!loading) _=SaveThemeAsync();
    }
    private async Task SaveThemeAsync()
    {
        try {await settings.SaveAsync(new(){Key="ThemeMode",Value=ThemeChoice}); ApplyTheme();}
        catch(Exception ex){Message=ex.Message;}
    }
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
    [RelayCommand] private void OpenDownload()
    {
        if(AvailableUpdate is not {} update)return;
        try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(update.DownloadUrl.AbsoluteUri){UseShellExecute=true});}
        catch(Exception ex){Message="無法開啟下載頁："+ex.Message;}
    }
}
