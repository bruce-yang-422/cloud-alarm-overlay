using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;
public sealed class MilestoneTwoTests
{
    [Fact] public async Task Admin_login_four_pages_locks_export_and_expiry_work_end_to_end()
    {
        await MilestoneOneTests.RunSta(async()=>{
            var paths=new Paths();var clock=new Clock();var dialogs=new Dialogs();var autoStart=new AutoStart();
            using var host=new HostBuilder().ConfigureServices(s=>{
                CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);s.AddSingleton<TimeProvider>(clock);
                s.AddSingleton<IUserDialogs>(dialogs);s.AddSingleton<ISheetCsvClient,FakeCsv>();s.AddSingleton<IAutoStartService>(autoStart);
            }).Build();
            autoStart.Session=host.Services.GetRequiredService<AdminSession>();
            MainWindow? window=null;AdminLoginWindow? login=null;
            try
            {
                await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
                await host.Services.GetRequiredService<IDeviceIdentityService>().SetInitialIdentityAsync("M2-TEST","驗收測試");
                var vm=host.Services.GetRequiredService<MainViewModel>();await vm.InitializeAsync();
                var loginRequested=false;
                void RequestLogin()=>loginRequested=true;
                vm.Admin.LoginRequested+=RequestLogin;
                vm.NavigationIndex=5;
                Assert.True(loginRequested);
                Assert.Equal(0,vm.PageIndex);
                Assert.Equal(0,vm.NavigationIndex);
                vm.Admin.LoginRequested-=RequestLogin;
                window=host.Services.GetRequiredService<MainWindow>();window.Show();window.UpdateLayout();
                vm.PageIndex=5;Assert.Equal(0,vm.PageIndex);Assert.True(vm.Admin.IsSignedOut);
                var navigation=(ItemsControl)window.FindName("PrimaryNavigation");
                Assert.Equal(new[]{"首頁","我的任務","歷史紀錄","番茄鐘","設定","管理者專區"},navigation.Items.Cast<NavigationItem>().Select(item=>item.Title));
                var navButtons=Descendants<Button>(navigation).ToArray();
                Assert.Equal(6,navButtons.Length);
                var builderNavigation=(ContentControl)window.FindName("TaskBuilderNavigation");
                var builderButton=Assert.Single(Descendants<Button>(builderNavigation));
                Assert.Equal(FontWeights.Normal,builderButton.FontWeight);
                Assert.All(navButtons.Skip(1),button=>Assert.Equal(FontWeights.Normal,button.FontWeight));
                var settingsButton=navButtons.Single(button=>((NavigationItem)button.DataContext).Title=="設定");
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(4,vm.PageIndex);
                Assert.Equal(FontWeights.SemiBold,settingsButton.FontWeight);
                vm.PageIndex=0;
                Assert.DoesNotContain(Descendants<Button>(window),b=>Equals(b.Content,"🔒 管理者登入")&&b.IsVisible);
                login=new AdminLoginWindow(host.Services.GetRequiredService<IAuthenticationService>()){Owner=window};
                Exception? loginFailure=null;
                login.Loaded+=async(_,_)=>{
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        ((TextBox)login.FindName("Username")).Text="test-admin";
                        ((PasswordBox)login.FindName("Password")).Password="Testing-1234";
                        ((PasswordBox)login.FindName("Confirmation")).Password="Testing-1234";
                        Assert.True(((Button)login.FindName("Submit")).IsEnabled);
                        Screenshot(login,"admin-first-setup");
                        ((Button)login.FindName("Submit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch(Exception ex){loginFailure=ex;login.Close();}
                };
                Assert.True(login.ShowDialog());
                Assert.Null(loginFailure);
                login=null;
                Assert.True(vm.Admin.Session.IsAuthenticated);
                Assert.True(vm.Admin.AutoStartEnabled);
                vm.Admin.AutoStartEnabled=false;vm.Admin.SaveAutoStart();
                Assert.False(autoStart.Enabled);
                vm.Admin.AutoStartEnabled=true;vm.Admin.SaveAutoStart();
                Assert.True(autoStart.Enabled);
                Assert.True(vm.Preferences.Maintenance.CanEditUpdateUrl);
                // Exiting still asks for credentials when an administrator is already signed in.
                var exitLogin=new AdminLoginWindow(host.Services.GetRequiredService<IAuthenticationService>(),forExit:true){Owner=window};
                Exception? exitLoginFailure=null;
                exitLogin.Loaded+=async(_,_)=>{
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert.Equal("結束程式驗證",((TextBlock)exitLogin.FindName("Heading")).Text);
                        Assert.Equal("驗證並結束",((Button)exitLogin.FindName("Submit")).Content);
                        ((TextBox)exitLogin.FindName("Username")).Text="test-admin";
                        ((PasswordBox)exitLogin.FindName("Password")).Password="wrong";
                        ((Button)exitLogin.FindName("Submit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var error=(TextBlock)exitLogin.FindName("Error");
                        for(var attempt=0;attempt<40&&error.Text.Length==0;attempt++)await Task.Delay(25);
                        Assert.True(exitLogin.IsVisible);
                        Assert.Equal("帳號或密碼錯誤",error.Text);
                        ((PasswordBox)exitLogin.FindName("Password")).Password="Testing-1234";
                        ((Button)exitLogin.FindName("Submit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch(Exception ex){exitLoginFailure=ex;exitLogin.Close();}
                };
                Assert.True(exitLogin.ShowDialog());
                Assert.Null(exitLoginFailure);
                await vm.Admin.SetExitPasswordAsync("Only-Exit-123");
                Assert.Contains("專用結束密碼",vm.Admin.ExitPasswordModeLabel);
                var dedicatedLogin=new ExitPasswordWindow(host.Services.GetRequiredService<IExitProtectionService>()){Owner=window};
                Exception? dedicatedFailure=null;
                dedicatedLogin.Loaded+=async(_,_)=>{
                    try
                    {
                        var password=(PasswordBox)dedicatedLogin.FindName("Password");
                        var submit=(Button)dedicatedLogin.FindName("Submit");
                        password.Password="wrong";
                        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var error=(TextBlock)dedicatedLogin.FindName("Error");
                        for(var attempt=0;attempt<40&&error.Text.Length==0;attempt++)await Task.Delay(25);
                        Assert.True(dedicatedLogin.IsVisible);
                        Assert.Equal("結束程式密碼錯誤。",error.Text);
                        password.Password="Only-Exit-123";
                        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch(Exception ex){dedicatedFailure=ex;dedicatedLogin.Close();}
                };
                Assert.True(dedicatedLogin.ShowDialog());
                Assert.Null(dedicatedFailure);
                await vm.Admin.UseAdministratorExitPasswordAsync();
                await vm.Admin.SessionChangedAsync();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(vm.Admin.IsAuthenticated);
                vm.PageIndex=4;window.UpdateLayout();
                ((TabControl)Descendants<PreferencesView>(window).Single().FindName("SettingsTabs")).SelectedIndex=4;
                window.UpdateLayout();
                Assert.Contains(Descendants<TextBox>(window),t=>System.Windows.Automation.AutomationProperties.GetName(t)=="更新資訊 HTTPS 連結"&&t.IsVisible&&t.IsEnabled);
                Assert.DoesNotContain(Descendants<Button>(window),b=>Equals(b.Content,"🔒 管理者登入")&&b.IsVisible);
                vm.OpenAdminCommand.Execute("0");
                window.UpdateLayout();
                Assert.Equal(5,vm.PageIndex);
                Assert.Equal(5,Assert.Single(vm.NavigationItems,item=>item.IsSelected).PageIndex);
                Assert.DoesNotContain(Descendants<Button>(window),b=>b.IsVisible&&b.Content is string label&&new[]{"同步來源","本機設定","系統紀錄","操作稽核","備份與還原"}.Contains(label));
                Assert.Contains(Descendants<Button>(window),b=>Equals(b.Content,"登出管理者")&&b.IsVisible);
                for(var i=0;i<5;i++)
                {
                    vm.Admin.SelectedTab=i;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.True(Descendants<AdminView>(window).Single().IsVisible);
                    Assert.Equal(5,Assert.Single(vm.NavigationItems,item=>item.IsSelected).PageIndex);
                    Assert.Equal(i is 2 or 3,vm.Admin.ShowsLogFilter);
                    if(i==0)
                    {
                        Assert.Contains(Descendants<TextBlock>(window),t=>Equals(t.Text,"同步間隔（Sheet A／B 共用）")&&t.IsVisible);
                        var intervalSlider=Assert.Single(Descendants<Slider>(window),s=>System.Windows.Automation.AutomationProperties.GetName(s)=="共用同步間隔秒數"&&s.IsVisible);
                        Assert.Equal(5,intervalSlider.TickFrequency);
                        intervalSlider.Value=50;
                        Assert.Equal(50,vm.IntervalSeconds);
                        Assert.Equal(2,Descendants<TextBlock>(window).Count(t=>Equals(t.Text,"連線狀態")&&t.IsVisible));
                        Assert.DoesNotContain(Descendants<TextBlock>(window),t=>Equals(t.Text,"›  連線測試輸出")&&t.IsVisible);
                    }
                    if(i==1)
                    {
                        Assert.Contains(Descendants<TextBlock>(window),t=>Equals(t.Text,"管理者帳號與密碼")&&t.IsVisible);
                        Assert.Contains(Descendants<Button>(window),b=>Equals(b.Content,"儲存管理者帳密")&&b.IsVisible);
                        Assert.Contains(Descendants<CheckBox>(window),c=>Equals(c.Content,"登入 Windows 後自動啟動程式")&&c.IsVisible);
                        Assert.Contains(Descendants<TextBlock>(window),t=>Equals(t.Text,"結束程式密碼保護")&&t.IsVisible);
                        Assert.Contains(Descendants<Button>(window),b=>Equals(b.Content,"設定／重設專用密碼")&&b.IsVisible);
                        Assert.Contains(Descendants<CheckBox>(window),c=>Equals(c.Content,"結束程式時要求密碼")&&c.IsVisible);
                    }
                    if(i==4){Assert.True(Descendants<BackupMaintenanceView>(window).Single().IsVisible);vm.Preferences.Maintenance.RequireAdministrator();}
                    Screenshot(window,"admin-page-"+i);
                }
                vm.Admin.SelectedTab=1;window.Width=1050;window.Height=680;window.UpdateLayout();
                Assert.Contains(Descendants<ScrollViewer>(window),s=>s.ScrollableHeight>0&&s.VerticalScrollBarVisibility==ScrollBarVisibility.Auto);
                window.Width=1280;window.Height=850;window.UpdateLayout();
                vm.Admin.LockFlash=true;vm.Admin.LockQuiet=true;vm.Admin.FlashMilliseconds=800;
                await vm.Admin.SavePolicyCommand.ExecuteAsync(null);
                Assert.False(vm.Preferences.CanEditFlash);Assert.False(vm.Preferences.CanEditQuiet);
                vm.PageIndex=4;window.UpdateLayout();Screenshot(window,"settings-locked");
                Assert.All(vm.Preferences.Sounds,r=>Assert.False(r.Enabled));
                vm.SheetAId="A";vm.TasksAGid="1";vm.HolidaysGid="2";vm.EmployeesGid="3";vm.LunarGid="4";
                await vm.TestSheetACommand.ExecuteAsync(null);
                Assert.Equal("部分失敗",vm.Admin.SheetATestState);
                Assert.Contains("失敗",vm.Admin.SheetAEmployeesStatus);
                Assert.Contains("成功",vm.Admin.SheetATasksStatus);
                Assert.StartsWith("最後測試：",vm.Admin.SheetATestedAt);
                Assert.Empty(await host.Services.GetRequiredService<ITaskRepository>().GetAllAsync());
                await vm.SyncCommand.ExecuteAsync(null);
                Assert.Contains("尚未儲存",vm.SyncMessage);
                await vm.Admin.RefreshLogsCommand.ExecuteAsync(null);
                Assert.Contains(vm.Admin.Audit,a=>a.Action=="FlashMilliseconds");
                await vm.Admin.ExportCommand.ExecuteAsync("AuditLog");
                Assert.StartsWith("AuditLog_",dialogs.Filename);
                Assert.StartsWith("CreatedAt,UserId,Action,OldValue,NewValue\r\n",dialogs.Csv);
                Assert.Contains("FlashMilliseconds",dialogs.Csv);
                vm.OpenAdminCommand.Execute("0");clock.Advance(TimeSpan.FromMinutes(15));
                Assert.True(vm.Admin.Session.CheckExpiry());await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.False(vm.Preferences.Maintenance.CanEditUpdateUrl);
                Assert.Equal(0,vm.PageIndex);Assert.False(vm.Admin.IsAuthenticated);Assert.Empty(vm.Admin.Audit);
                await vm.SaveSettingsCommand.ExecuteAsync(null);
                Assert.Contains("登入",vm.Status);
                Assert.Equal("",(await host.Services.GetRequiredService<SyncConfiguration>().LoadAsync()).SheetAId);
                vm.PageIndex=4;window.UpdateLayout();
                ((TabControl)Descendants<PreferencesView>(window).Single().FindName("SettingsTabs")).SelectedIndex=4;
                window.UpdateLayout();
                Assert.Contains(Descendants<TextBox>(window),t=>System.Windows.Automation.AutomationProperties.GetName(t)=="更新資訊 HTTPS 連結"&&t.IsVisible&&!t.IsEnabled);
                vm.PageIndex=5;Assert.Equal(0,vm.PageIndex);
            }
            finally{login?.Close();window?.ForceClose();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
        });
    }
    [Fact] public void Audit_CSV_escapes_quotes_commas_and_newlines_and_keeps_empty_headers()
    {
        var csv=CsvExport.Build(["CreatedAt","UserId","Action","OldValue","NewValue"],[["2026-09-15 12:00:00","admin","change","a,\"b\"\r\nc","新值"]]);
        Assert.Contains("\"a,\"\"b\"\"\r\nc\"",csv);
        Assert.Equal("CreatedAt\r\n",CsvExport.Build(["CreatedAt"],[]));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T typed)yield return typed;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
    private static void Screenshot(Window window,string name)
    {
        var folder=Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR");if(folder is null)return;
        window.UpdateLayout();var content=(FrameworkElement)window.Content;
        var dpi=VisualTreeHelper.GetDpi(content);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(content.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));
        bitmap.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
    }
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmOverlay.M2.AppTests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
    private sealed class AutoStart:IAutoStartService
    {
        public AdminSession Session {get;set;}=null!;
        public bool Enabled {get;private set;}=true;
        public bool IsEnabled()=>Enabled;
        public void SetEnabled(bool enabled){Session.RequireAdmin();Enabled=enabled;}
    }
    private sealed class Clock:TimeProvider
    {
        private long time;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>time;
        public void Advance(TimeSpan duration)=>time+=duration.Ticks;
    }
    private sealed class Dialogs:IUserDialogs
    {
        public string Csv="",Filename="";
        public bool Confirm(string message)=>true;
        public void Edit(AlarmTask? task,bool copy){}
        public void Export(string contents)=>Csv=contents;
        public void ExportNamed(string contents,string filename){Csv=contents;Filename=filename;}
    }
    private sealed class FakeCsv:ISheetCsvClient
    {
        public Task<string> DownloadAsync(string id,string gid,CancellationToken ct=default)=>gid switch{
            "1"=>Task.FromResult("Id,Time,Title,Enabled\r\n1,2026-09-16 12:00:00,測試,TRUE"),
            "2"=>Task.FromResult("Date,Type\r\n2026-09-16,國定假日"),
            "3"=>Task.FromException<string>(new IOException("模擬 Employees 失敗")),
            _=>Task.FromResult("Date,LunarDay\r\n2026-09-16,6")};
    }
}
