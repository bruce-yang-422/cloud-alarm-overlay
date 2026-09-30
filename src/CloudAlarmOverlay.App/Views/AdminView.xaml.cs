using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;
using Microsoft.Win32;

namespace CloudAlarmOverlay.App.Views;
public partial class AdminView:UserControl
{
    private MainViewModel? vm;
    private readonly AdminDraftState drafts=new();
    private readonly DispatcherTimer sessionTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private bool saving,googleImportBusy,narrow,navigationOpen;
    public AdminView()
    {
        InitializeComponent();
        sessionTimer.Tick+=(_,_)=>{vm?.Admin.Session.CheckExpiry();UpdateDraftFooter();};
    }
    private void WorkspaceLoaded(object sender,RoutedEventArgs e)
    {
        if(vm is null&&DataContext is MainViewModel model)
        {
            vm=model;
            drafts.Track(1,vm,nameof(vm.SheetAId),nameof(vm.TasksAGid),nameof(vm.HolidaysGid),nameof(vm.EmployeesGid),nameof(vm.LunarGid),nameof(vm.SheetBId),nameof(vm.TasksBGid),nameof(vm.IntervalSeconds));
            drafts.Track(2,vm.Admin.CalendarData,"Enabled","Frequency","HolidaysUrl","LunarUrl");
            drafts.Track(3,vm.Admin,"LockFlash","LockQuiet","AllowUrgentSnooze","FlashMilliseconds");
            if(vm.Preferences.Weather is {} weather)drafts.Track(3,weather,"SelectedCounty","SelectedDistrict");
            drafts.Track(4,vm.Admin,"ExitPasswordRequired");
            drafts.Track(5,vm.Admin,"AutoStartEnabled");drafts.Track(5,vm.Preferences.Maintenance,"UpdateUrl");
            drafts.Track(8,vm.Admin,"LogRetentionDaysText");
            drafts.Changed+=UpdateDraftFooter;
            vm.Admin.CanLeavePage=CanLeavePage;vm.CanLeaveAdministration=()=>CanLeavePage(vm.Admin.SelectedTab);
            vm.Admin.ReloadStarting+=drafts.BeginReload;vm.Admin.ReloadFinished+=drafts.EndReload;
            vm.AdminSettingsImported+=()=>{foreach(var page in new[]{1,2,3,4,5,8})drafts.Accept(page);};
            vm.Admin.CalendarData.SettingsLoaded+=()=>drafts.Accept(2);
            vm.Admin.CalendarData.HasPendingChanges=()=>drafts.IsDirty(2);
            vm.Admin.PropertyChanged+=AdminChanged;
            CalendarPage.SaveSettingsButton.Visibility=Visibility.Collapsed;
            CalendarPage.OperationResult.Visibility=Visibility.Collapsed;
            RouteOperationResult(vm.Admin.CalendarData,()=>vm.Admin.CalendarData.Message,2);
            RouteOperationResult(vm.Preferences.Maintenance,()=>vm.Preferences.Maintenance.Message,5,6);
            if(vm.Preferences.Google is {} google)RouteOperationResult(google,()=>google.Message,4);
        }
        sessionTimer.Start();ApplyNavigationLayout();UpdateDraftFooter();
    }
    private void WorkspaceUnloaded(object sender,RoutedEventArgs e){sessionTimer.Stop();navigationOpen=false;ApplyNavigationLayout();}
    private void RouteOperationResult(INotifyPropertyChanged source,Func<string> message,params int[] pages)
    {
        source.PropertyChanged+=(_,e)=>
        {
            if(e.PropertyName=="Message"&&vm is not null&&pages.Contains(vm.Admin.SelectedTab)&&vm.Admin.IsAuthenticated)
                vm.Admin.Message=message();
        };
    }
    private void AdminChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(AdminViewModel.SelectedTab))
        {
            navigationOpen=false;ApplyNavigationLayout();GoogleLoginAdvanced.IsExpanded=false;
            // Results belong to the page where the operation was performed.
            if(vm is not null)
            {
                vm.Admin.Message="";
                vm.Admin.CalendarData.Message="";vm.Preferences.Maintenance.Message="";
                if(vm.Preferences.Google is {} google)google.Message="";
            }
        }
        if(e.PropertyName==nameof(AdminViewModel.IsAuthenticated)&&vm?.Admin.IsAuthenticated==false)GoogleLoginAdvanced.IsExpanded=false;
        UpdateDraftFooter();
    }
    private bool CanLeavePage(int page)
    {
        if(vm is null||!vm.Admin.Session.IsAuthenticated)return true;
        if(saving)return false;
        if(!drafts.IsDirty(page))return true;
        if(!vm.Admin.ConfirmDiscardChanges())return false;
        drafts.Discard(page);return true;
    }
    private void UpdateDraftFooter()
    {
        if(vm is null)return;
        DraftFooter.Visibility=drafts.IsDirty(vm.Admin.SelectedTab)?Visibility.Visible:Visibility.Collapsed;
        DraftFooter.IsEnabled=vm.Admin.IsAuthenticated&&!saving;
        DraftActions.MaxWidth=vm.Admin.SelectedTab==1?1040:760;
        SaveDraftButton.Content=vm.Admin.SelectedTab==1?"儲存並同步":"儲存變更";
    }
    private void Navigate(object sender,RoutedEventArgs e){if(vm is not null&&sender is FrameworkElement {Tag:string index})vm.Admin.SelectedTab=int.Parse(index);}
    private void ToggleNavigation(object sender,RoutedEventArgs e){navigationOpen=!navigationOpen;ApplyNavigationLayout();}
    private void ResizeWorkspace(object sender,SizeChangedEventArgs e){narrow=ActualWidth<=850;ApplyNavigationLayout();}
    private void ApplyNavigationLayout()
    {
        if(NavigationPanel is null)return;
        NavigationColumn.Width=new(narrow?0:184);NavigationPanel.Width=narrow?208:double.NaN;
        NavigationPanel.HorizontalAlignment=narrow?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
        Grid.SetColumnSpan(NavigationPanel,narrow?2:1);
        NavigationPanel.Visibility=!narrow||navigationOpen?Visibility.Visible:Visibility.Collapsed;
        NavigationToggle.Visibility=narrow?Visibility.Visible:Visibility.Collapsed;
        AccessCaption.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
    }
    private void ResizeSourceCards(object sender,SizeChangedEventArgs e)
    {
        var single=SourceCards.ActualWidth<620;
        Grid.SetColumnSpan(SourceA,single?3:1);Grid.SetColumnSpan(SourceB,single?3:1);
        Grid.SetColumn(SourceB,single?0:2);Grid.SetRow(SourceB,single?2:0);
    }
    private async void SaveDraft(object sender,RoutedEventArgs e)
    {
        if(vm is null||saving)return;
        var page=vm.Admin.SelectedTab;saving=true;WorkspaceGrid.IsEnabled=false;UpdateDraftFooter();
        try
        {
            vm.Admin.Session.RequireAdmin();
            switch(page)
            {
                case 1:await vm.SaveAdminSourceDraftAsync();break;
                case 2:await vm.Admin.CalendarData.SaveDraftAsync();break;
                case 3:await vm.Admin.SavePoliciesAsync(vm.Preferences.Weather is {} weather&&drafts.IsDirty(3,weather));break;
                default:await vm.Admin.SaveWorkspaceSectionAsync(page);break;
            }
            drafts.Accept(page);
        }
        catch(Exception ex){vm.Admin.Message="儲存未完成："+ex.Message;}
        finally{saving=false;WorkspaceGrid.IsEnabled=true;UpdateDraftFooter();}
    }
    private void DiscardDraft(object sender,RoutedEventArgs e){if(vm is not null)drafts.Discard(vm.Admin.SelectedTab);}
    private async void LoadGoogleLoginSettings(object sender,RoutedEventArgs e)
    {
        if(DataContext is MainViewModel model&&model.Admin.Session.IsAuthenticated&&model.Preferences.Google is {} google)await google.LoadAsync();
    }
    private async void ImportGoogleClient(object sender,RoutedEventArgs e)
    {
        if(googleImportBusy||DataContext is not MainViewModel model||model.Preferences.Google is not {} google||!google.CanAct)return;
        googleImportBusy=true;
        try
        {
            model.Admin.Session.RequireAdmin();
            var picker=new OpenFileDialog{Filter="Google OAuth JSON|*.json",Title="匯入 Google 桌面 OAuth 設定"};
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            model.Admin.Session.RequireAdmin();
            if(new FileInfo(picker.FileName).Length>64*1024)throw new InvalidDataException("設定檔大小超過限制。");
            await google.ImportAsync(await File.ReadAllTextAsync(picker.FileName));
        }
        catch(UnauthorizedAccessException){google.Message="請先登入管理者；登入逾時請重新驗證。";}
        catch(Exception){google.Message="無法讀取設定檔，請確認為 Google 桌面 OAuth JSON。";}
        finally{googleImportBusy=false;}
    }
    private void OpenGoogleLoginGuide(object sender,RoutedEventArgs e)
    {
        if(vm is null||!vm.Admin.Session.IsAuthenticated)return;
        new GoogleActionDialog("自訂 Google 登入","組織專用桌面 OAuth 設定",
            "一般使用者直接使用程式內建的 Google 登入，不需要自行準備 JSON。\n\n僅在組織要求使用自己的 Google Cloud 專案時：\n1. 建立專案並啟用 Google Sheets API 與 Google Calendar API。\n2. 設定 Google Auth Platform 同意畫面。\n3. 建立「桌面應用程式」OAuth 用戶端。\n4. 下載 JSON，登出所有 Google 帳號後匯入。\n\n這不是服務帳戶私鑰。", "知道了",information:true){Owner=Window.GetWindow(this)}.ShowDialog();
    }
    private void ChangeAdminCredentials(object sender,RoutedEventArgs e)
    {
        if(vm is null)return;
        new AdminCredentialsDialog("admin",vm.Admin.AdminUsername,()=>vm.Admin.Session.RequireAdmin(),async input=>
        {
            var old=vm.Admin.AdminUsername;vm.Admin.AdminUsername=input.Username;
            try{await vm.Admin.ChangeCredentialsAsync(input.CurrentPassword,input.NewPassword);}
            catch{vm.Admin.AdminUsername=old;throw;}
        }){Owner=Window.GetWindow(this)}.ShowDialog();
    }
    private void SetExitPassword(object sender,RoutedEventArgs e)
    {
        if(vm is null)return;
        new AdminCredentialsDialog("exit","",()=>vm.Admin.Session.RequireAdmin(),input=>vm.Admin.SetExitPasswordAsync(input.NewPassword)){Owner=Window.GetWindow(this)}.ShowDialog();
    }
    private async void UseAdminExitPassword(object sender,RoutedEventArgs e)
    {
        if(vm is null)return;
        try
        {
            vm.Admin.Session.RequireAdmin();
            if(new GoogleActionDialog("改用管理者帳密？","結束程式保護","從系統匣結束程式時，會改用管理者帳密驗證。","改用管理者帳密"){Owner=Window.GetWindow(this)}.ShowDialog()!=true)return;
            await vm.Admin.UseAdministratorExitPasswordAsync();
        }
        catch(Exception ex){vm.Admin.Message=ex.Message;}
    }
    private async void ResetAll(object sender,RoutedEventArgs e)
    {
        if(Application.Current is App app)await app.RequestFactoryResetAsync();
    }
}
