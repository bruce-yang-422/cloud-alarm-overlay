using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.Services;
namespace CloudAlarmOverlay.App.ViewModels;

public partial class MaintenanceViewModel(IBackupRestoreService backups, IUpdateCheckService updates, ISettingsRepository settings, IThemeService theme, AdminSession session, IUpdateInstallerService installer, IUserDialogs dialogs) : ObservableObject
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
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool busy;
    [ObservableProperty] private string updateMessage="按下檢查更新，查看公開版本與更新內容。";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRelease))]
    [NotifyPropertyChangedFor(nameof(ReleaseHeading))]
    [NotifyPropertyChangedFor(nameof(ReleaseNotes))]
    [NotifyPropertyChangedFor(nameof(DownloadLabel))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdate))]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    [NotifyCanExecuteChangedFor(nameof(OpenDownloadCommand))]
    private UpdateInfo? latestRelease;
    public bool CanCheckUpdate=>!Busy;
    public bool CanDownloadUpdate=>!Busy;
    public bool CanInstallUpdate=>!Busy&&DownloadedUpdate is not null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadedUpdate))]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private DownloadedUpdate? downloadedUpdate;
    public bool HasDownloadedUpdate=>DownloadedUpdate is not null;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    private bool isDownloading;
    [ObservableProperty] private bool downloadIndeterminate=true;
    [ObservableProperty] private double downloadPercent;
    [ObservableProperty] private string downloadProgressText="";
    private CancellationTokenSource? downloadCancellation;
    public bool HasRelease=>LatestRelease is not null;
    public string ReleaseHeading=>LatestRelease is {} info?$"公開版本 {info.LatestVersion}":"更新內容";
    public string ReleaseNotes=>ReleaseNotesFormatter.Format(LatestRelease?.ReleaseNote);
    public string DownloadLabel=>LatestRelease is {} info?$"下載並驗證 v{info.LatestVersion}":"下載並驗證安裝檔";
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
        downloadCancellation?.Cancel();
        LatestRelease=null;DownloadedUpdate=null;
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
        Busy=true;LatestRelease=null;DownloadedUpdate=null;DownloadIndeterminate=true;UpdateMessage="正在檢查更新…";
        try
        {
            LatestRelease=await updates.GetManifestAsync();
            UpdateMessage=CompareVersion(LatestRelease.LatestVersion) switch
            {>0=>"有新版本可下載，更新內容如下。",<0=>"目前使用的版本比公開版新。下方顯示最新公開版的更新內容。",_=>"目前已是最新版本，仍可查看更新內容。"};
        }
        catch(Exception ex){UpdateMessage="無法檢查更新："+ex.Message;}
        finally {Busy=false;}
    }
    [RelayCommand(CanExecute=nameof(CanDownloadUpdate))] private async Task OpenDownloadAsync()
    {
        if(!CanDownloadUpdate)return;
        using var cancellation=new CancellationTokenSource();downloadCancellation=cancellation;
        Busy=true;IsDownloading=true;DownloadedUpdate=null;DownloadPercent=0;DownloadIndeterminate=true;
        DownloadProgressText="正在取得更新資訊…";UpdateMessage="正在下載更新…";
        try
        {
            var update=await updates.GetManifestAsync(cancellation.Token);
            LatestRelease=update;
            var progress=new Progress<UpdateDownloadProgress>(value=>
            {
                if(!IsDownloading||downloadCancellation!=cancellation)return;
                DownloadIndeterminate=value.TotalBytes is null||value.IsVerifying;
                DownloadPercent=value.TotalBytes is >0?Math.Clamp(value.BytesReceived*100d/value.TotalBytes.Value,0,100):0;
                DownloadProgressText=value.IsVerifying?"正在驗證 SHA-256…":value.TotalBytes is {} total
                    ?$"{value.BytesReceived/1048576d:F1} / {total/1048576d:F1} MB（{DownloadPercent:F0}%）"
                    :$"已下載 {value.BytesReceived/1048576d:F1} MB";
            });
            var downloaded=await installer.DownloadAsync(update,progress,cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            DownloadedUpdate=downloaded;
            IsDownloading=false;
            await OfferInstallAsync();
        }
        catch(OperationCanceledException){UpdateMessage=cancellation.IsCancellationRequested?"已取消下載，未完成的安裝檔已清除。":"更新下載逾時，請稍後重新下載。";}
        catch(Exception ex){DownloadedUpdate=null;UpdateMessage="更新未完成："+ex.Message;}
        finally {IsDownloading=false;downloadCancellation=null;Busy=false;}
    }
    [RelayCommand(CanExecute=nameof(IsDownloading))] private void CancelDownload()=>downloadCancellation?.Cancel();
    [RelayCommand(CanExecute=nameof(CanInstallUpdate))] private async Task InstallUpdateAsync()
    {
        if(!CanInstallUpdate)return;
        Busy=true;
        try{await OfferInstallAsync();}
        catch(InvalidDataException ex){DownloadedUpdate=null;UpdateMessage=ex.Message;}
        catch(Exception ex){UpdateMessage="無法啟動安裝程式："+ex.Message;}
        finally{Busy=false;}
    }
    private async Task OfferInstallAsync()
    {
        if(DownloadedUpdate is not {} downloaded)return;
        UpdateMessage="安裝檔已下載並通過 SHA-256 驗證，可立即安裝或稍後再安裝。";
        if(!dialogs.ConfirmAdminAction("安裝更新",$"v{downloaded.Info.LatestVersion} 安裝檔已通過 SHA-256 驗證。\n\n現在安裝？請先儲存編輯內容，安裝程式將關閉目前程式並引導你完成更新。","立即安裝"))return;
        DownloadIndeterminate=true;UpdateMessage="正在重新驗證安裝檔並啟動安裝程式…";
        await installer.InstallAsync(downloaded);
        DownloadedUpdate=null;
        UpdateMessage="已啟動安裝程式，請依安裝精靈完成更新。";
    }
}
