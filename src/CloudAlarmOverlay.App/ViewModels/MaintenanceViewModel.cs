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
    public bool IsCompanyMode=>!session.IsPersonal;
    public async Task AdminSessionChangedAsync()
    {
        OnPropertyChanged(nameof(CanEditUpdateUrl));
        OnPropertyChanged(nameof(IsCompanyMode));
        SaveUpdateUrlCommand.NotifyCanExecuteChanged();
        UpdateUrl=(await settings.GetAsync("UpdateManifestUrl"))?.Value??"";
    }
    public string VersionLabel => "目前版本 "+UpdateCheckService.CurrentVersion.ToString(3);
    [ObservableProperty] private string message="";
    [ObservableProperty] private string updateUrl="";
    [ObservableProperty] private string themeChoice="跟隨系統";
    [ObservableProperty] private string themeColorChoice="預設";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckUpdate))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDownloadCommand))]
    private bool busy;
    [ObservableProperty] private string updateMessage="按下檢查更新，查看公開版本與更新內容。";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRelease))]
    [NotifyPropertyChangedFor(nameof(ReleaseHeading))]
    [NotifyPropertyChangedFor(nameof(ReleaseNotes))]
    [NotifyPropertyChangedFor(nameof(DownloadLabel))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdate))]
    private UpdateInfo? latestRelease;
    public bool CanCheckUpdate=>!Busy;
    public bool HasRelease=>LatestRelease is not null;
    public string ReleaseHeading=>LatestRelease is {} info?$"公開版本 {info.LatestVersion}":"更新內容";
    public string ReleaseNotes=>ReleaseNotesFormatter.Format(LatestRelease?.ReleaseNote);
    public string DownloadLabel=>LatestRelease is {} info?$"下載 v{info.LatestVersion} 安裝檔":"下載安裝檔";
    [ObservableProperty] private bool keepWindowAspectRatio=true;
    partial void OnKeepWindowAspectRatioChanged(bool value)
    {
        if(!loading)_=SaveWindowBehaviorAsync(value);
    }
    private bool committedAspect=true;
    private readonly SemaphoreSlim aspectSaveGate=new(1,1);
    private async Task SaveWindowBehaviorAsync(bool value)
    {
        await aspectSaveGate.WaitAsync();
        try{await settings.SaveAsync(new(){Key="KeepWindowAspectRatio",Value=value.ToString()});committedAspect=value;}
        catch(Exception ex)
        {
            if(KeepWindowAspectRatio==value){loading=true;try{KeepWindowAspectRatio=committedAspect;}finally{loading=false;}}
            Message="視窗設定儲存失敗："+ex.Message;
        }
        finally{aspectSaveGate.Release();}
    }
    public string[] Themes {get;}=["淺色","暗色","跟隨系統"];
    public string[] ThemeColors {get;}=["預設","櫻花粉","若竹綠","薰衣草紫","夕陽橘","極簡銀白"];
    public UpdateInfo? AvailableUpdate=>LatestRelease is {} info&&CompareVersion(info.LatestVersion)>0?info:null;
    private static int CompareVersion(Version version)=>new Version(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision)).CompareTo(UpdateCheckService.CurrentVersion);
    partial void OnUpdateUrlChanged(string value)
    {
        LatestRelease=null;
        UpdateMessage="按下檢查更新，查看公開版本與更新內容。";
    }
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
            committedTheme=ThemeChoice;committedColor=ThemeColorChoice;committedAspect=KeepWindowAspectRatio;
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
    private string committedTheme="跟隨系統",committedColor="預設";
    private async Task SaveThemeAsync()
    {
        await themeSaveGate.WaitAsync();
        var requestedMode=ThemeChoice;var requestedColor=ThemeColorChoice;
        try
        {
            await settings.SaveAsync(new(){Key="ThemeColorStyle",Value=requestedColor});committedColor=requestedColor;
            await settings.SaveAsync(new(){Key="ThemeMode",Value=requestedMode});committedTheme=requestedMode;
            ApplyTheme();
        }
        catch(Exception ex)
        {
            if(ThemeChoice==requestedMode&&ThemeColorChoice==requestedColor)
            {
                loading=true;
                try{ThemeChoice=committedTheme;ThemeColorChoice=committedColor;ApplyTheme();}
                finally{loading=false;}
            }
            Message="外觀設定未完整儲存："+ex.Message;
        }
        finally{themeSaveGate.Release();}
    }
    private readonly SemaphoreSlim themeSaveGate=new(1,1);
    [RelayCommand(CanExecute=nameof(CanEditUpdateUrl))] private async Task SaveUpdateUrlAsync()
    {
        try {session.RequireAdmin(); if(!string.IsNullOrWhiteSpace(UpdateUrl))UpdateCheckService.ValidateUrl(UpdateUrl); await settings.SaveAsync(new(){Key="UpdateManifestUrl",Value=UpdateUrl.Trim()}); Message="更新來源已儲存。";}
        catch(Exception ex){Message=ex.Message;}
    }
    [RelayCommand(CanExecute=nameof(CanCheckUpdate))] public async Task CheckUpdateAsync()
    {
        if(Busy)return;
        Busy=true;LatestRelease=null;UpdateMessage="正在檢查更新…";
        try
        {
            LatestRelease=await updates.GetManifestAsync();
            UpdateMessage=CompareVersion(LatestRelease.LatestVersion) switch
            {>0=>"有新版本可下載，更新內容如下。",<0=>"目前使用的版本比公開版新。下方顯示最新公開版的更新內容。",_=>"目前已是最新版本，仍可查看更新內容。"};
        }
        catch(Exception ex){UpdateMessage="無法檢查更新："+ex.Message;}
        finally {Busy=false;}
    }
    [RelayCommand(CanExecute=nameof(CanCheckUpdate))] private async Task OpenDownloadAsync()
    {
        if(Busy)return;
        Busy=true;UpdateMessage="正在取得下載連結…";
        try
        {
            var update=await updates.GetManifestAsync();
            LatestRelease=update;
            browser.Open(update.DownloadUrl);
            UpdateMessage="已交由瀏覽器下載安裝檔。下載完成後，開啟安裝檔進行更新。";
        }
        catch(Exception ex){UpdateMessage="無法啟動安裝檔下載："+ex.Message;}
        finally {Busy=false;}
    }
}
