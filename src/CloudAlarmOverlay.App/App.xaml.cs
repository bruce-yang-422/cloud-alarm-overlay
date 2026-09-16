using System.Diagnostics;
using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
namespace CloudAlarmOverlay.App;
public partial class App:Application
{
    private IHost? host;
    private Mutex? instance;
    private bool stopping;
    private bool exitConfirmationOpen;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance=new Mutex(true,@"Local\CloudAlarmOverlay",out var created);
        if(!created){MessageBox.Show("Cloud Alarm Overlay 已在執行，請由系統匣開啟。");Shutdown();return;}
        try
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            new FactoryReset(new CloudAlarmOverlay.Infrastructure.AppPaths().DataDirectory).ApplyPending();
            host=CompositionRoot.CreateHost();
            await host.StartAsync();
            await host.Services.GetRequiredService<ISystemEventStore>().AppendAsync(new(){EventType="AppStart",Message="程式啟動"});
            var maintenance=host.Services.GetRequiredService<MaintenanceViewModel>();
            await maintenance.InitializeAsync();
            var identity=host.Services.GetRequiredService<IDeviceIdentityService>();
            if(await identity.GetLocalAsync() is null && new IdentityWindow(identity,host.Services.GetRequiredService<IBackupRestoreService>()).ShowDialog()!=true)
            {await StopAsync();return;}
            var window=host.Services.GetRequiredService<MainWindow>();MainWindow=window;
            await host.Services.GetRequiredService<MainViewModel>().InitializeAsync();
            window.StartTray(host.Services.GetRequiredService<ChangeSignal>(),RequestExitAsync);
            SystemEvents.PowerModeChanged+=PowerChanged;
            host.Services.GetRequiredService<ChangeSignal>().Notify();
            window.Show();
            _ = CheckUpdateOnStartupAsync(maintenance);
        }
        catch(Exception ex)
        {
            Trace.TraceError(ex.ToString());
            MessageBox.Show($"無法啟動 Cloud Alarm Overlay。\n{ex.Message}","啟動失敗",MessageBoxButton.OK,MessageBoxImage.Error);
            await StopAsync(1);
        }
    }
    private async Task CheckUpdateOnStartupAsync(MaintenanceViewModel maintenance)
    {
        if(string.IsNullOrWhiteSpace(maintenance.UpdateUrl))return;
        await maintenance.CheckUpdateAsync();
        if(maintenance.AvailableUpdate is {} info && !stopping)
            MessageBox.Show($"有新版本 {info.LatestVersion}。請至「設定 → 外觀與資料」開啟下載頁。\n{info.ReleaseNote}","更新通知",MessageBoxButton.OK,MessageBoxImage.Information);
    }
    private void PowerChanged(object sender,PowerModeChangedEventArgs e)
    {if(e.Mode==PowerModes.Resume)host?.Services.GetRequiredService<RuntimeState>().Resume();}
    private async Task RequestExitAsync()
    {
        if(stopping||exitConfirmationOpen)return;
        exitConfirmationOpen=true;
        try
        {
            if(MainWindow is not MainWindow window)return;
            window.Open();
            if(host?.Services.GetRequiredService<AlarmPresenter>().HasBlockingNotification==true)
            {MessageBox.Show(window,"請先完成目前的緊急提醒／強制通知確認，再結束程式。");return;}
            var answer=MessageBox.Show(window,"結束程式後不會顯示提醒。確定結束？","結束 Cloud Alarm Overlay",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No);
            if(answer!=MessageBoxResult.Yes)return;
            // An existing admin session is not enough to quit: require the configured password on every exit.
            var protection=host!.Services.GetRequiredService<IExitProtectionService>();
            var verified=!await protection.IsRequiredAsync() || (await protection.GetModeAsync()=="Dedicated"
                ?new ExitPasswordWindow(protection){Owner=window}.ShowDialog()==true
                :new AdminLoginWindow(host.Services.GetRequiredService<IAuthenticationService>(),forExit:true){Owner=window}.ShowDialog()==true);
            if(!verified)return;
            // A blocking notification may have arrived while the confirmation was open.
            if(host?.Services.GetRequiredService<AlarmPresenter>().HasBlockingNotification==true)
            {MessageBox.Show(window,"有新的緊急提醒／強制通知，請先完成確認再結束程式。");return;}
            await StopAsync();
        }
        finally {exitConfirmationOpen=false;}
    }
    public async Task RequestFactoryResetAsync()
    {
        if(stopping||exitConfirmationOpen||host is null)return;
        exitConfirmationOpen=true;
        try
        {
            var session=host.Services.GetRequiredService<AdminSession>();
            if(!session.IsAuthenticated && new AdminLoginWindow(host.Services.GetRequiredService<IAuthenticationService>()){Owner=MainWindow}.ShowDialog()!=true)return;
            session.RequireAdmin();
            if(host.Services.GetRequiredService<AlarmPresenter>().HasBlockingNotification)
            {MessageBox.Show(MainWindow,"請先完成緊急提醒／強制通知確認，再重置。");return;}
            var answer=MessageBox.Show(MainWindow,
                "將永久清除本機所有任務、同步來源、個人設定、管理者帳密、裝置身分、番茄鐘資料、歷史與日誌。\n\n程式會關閉，下次啟動清除資料並回到首次設定。其他位置的匯出備份不會刪除。\n\n此操作無法復原，確定重置？",
                "恢復初始狀態",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No);
            if(answer!=MessageBoxResult.Yes)return;
            session.RequireAdmin();
            if(host.Services.GetRequiredService<AlarmPresenter>().HasBlockingNotification)
            {MessageBox.Show(MainWindow,"有新的緊急提醒／強制通知，請先完成確認。");return;}
            host.Services.GetRequiredService<IAutoStartService>().SetEnabled(true);
            new FactoryReset(host.Services.GetRequiredService<IAppPaths>().DataDirectory).Request();
            await StopAsync();
        }
        catch(Exception ex){MessageBox.Show(MainWindow,"無法重置："+ex.Message,"恢復初始狀態",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally{exitConfirmationOpen=false;}
    }
    private async Task StopAsync(int code=0)
    {
        if(stopping)return;stopping=true;
        SystemEvents.PowerModeChanged-=PowerChanged;
        try {if(host is not null){await host.Services.GetRequiredService<ISystemEventStore>().AppendAsync(new(){EventType="AppExit",Message="程式結束"});await host.StopAsync(TimeSpan.FromSeconds(10));}}
        catch(Exception ex){Trace.TraceError(ex.ToString());code=1;}
        (MainWindow as MainWindow)?.ForceClose();
        host?.Dispose();host=null;
        Shutdown(code);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged-=PowerChanged;
        host?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
