using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Core.Models;
using Hardcodet.Wpf.TaskbarNotification;
namespace CloudAlarmOverlay.App.Views;
public partial class MainWindow:Window
{
    private TaskbarIcon? tray;
    private bool allowClose;
    private readonly MainViewModel vm;
    private ChangeSignal? signal;
    private readonly DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    private readonly DispatcherTimer countdownTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly IAuthenticationService authentication;
    private readonly TaskBuilderHostService taskBuilderHost;
    private readonly DispatcherTimer idleTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    public MainWindow(MainViewModel viewModel,IAuthenticationService authentication,TaskBuilderHostService taskBuilderHost)
    {
        InitializeComponent();DataContext=vm=viewModel;this.authentication=authentication;this.taskBuilderHost=taskBuilderHost;
        vm.TaskSelectionRestored+=RestoreTaskSelection;
        Closed+=(_,_)=>vm.TaskSelectionRestored-=RestoreTaskSelection;
        SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowSizing);
        // WPF work-area dimensions are device-independent units, including display scaling.
        // Leave the taskbar available and use the full work area on shorter displays.
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(1280, workArea.Width);
        Height = Math.Min(980, workArea.Height);
        if (workArea.Height < 1000 || workArea.Width < 1300)
            WindowState = WindowState.Maximized;
        vm.Admin.LoginRequested+=Login;vm.Admin.Session.Changed+=SessionChanged;
        InputManager.Current.PreProcessInput+=OnInput;
        idleTimer.Tick+=(_,_)=>vm.Admin.Session.CheckExpiry();idleTimer.Start();
        Closing+=(_,e)=>{if(tray is not null&&!allowClose){e.Cancel=true;Hide();}};
        Closed+=(_,_)=>{idleTimer.Stop();InputManager.Current.PreProcessInput-=OnInput;vm.Admin.LoginRequested-=Login;vm.Admin.Session.Changed-=SessionChanged;refreshTimer.Stop();countdownTimer.Stop();if(signal is not null)signal.Changed-=OnDataChanged;tray?.Dispose();};
    }
    private void TaskTitleDoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount!=2 || (sender as FrameworkElement)?.DataContext is not TaskRow row)return;
        e.Handled=true;
        new TaskPreviewWindow(new(row.Task)){Owner=this}.ShowDialog();
    }
    public void StartTray(ChangeSignal changes,Func<Task> exit)
    {
        signal=changes;signal.Changed+=OnDataChanged;
        var menu=new ContextMenu();
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source=new Uri("pack://application:,,,/CloudAlarmOverlay.App;component/Styles/TrayMenu.xaml")
        });
        void Add(string name,Action action){var item=new MenuItem{Header=name};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Add("開啟主視窗",Open);
        menu.Items.Add(new Separator());
        Add("立即同步",()=>vm.SyncCommand.Execute(null));
        Add("新增任務",()=>{Open();vm.NewTaskCommand.Execute(null);});
        Add("開啟任務產生器",async()=>await taskBuilderHost.OpenInBrowserAsync());
        var previews=new MenuItem{Header="測試通知"};
        foreach(var level in new[]{AlarmLevels.Low,AlarmLevels.Mid,AlarmLevels.High,AlarmLevels.Max})
        {var item=new MenuItem{Header=CloudAlarmOverlay.App.Styles.AlarmLevelLabelConverter.Label(level),Command=vm.PreviewCommand,CommandParameter=level};previews.Items.Add(item);}
        menu.Items.Add(previews);
        Add("設定",()=>{vm.PageIndex=4;Open();});
        Add("管理者登入",()=>{Open();if(vm.Admin.Session.IsAuthenticated)vm.OpenAdminCommand.Execute("0");else Login();});
        Add("登出管理者",()=>vm.Admin.LogoutCommand.Execute(null));
        Add("關於",()=>MessageBox.Show("Cloud Alarm Overlay\nMilestone 2 · 本機管理與公開 CSV 提醒\n關閉主視窗後仍會在系統匣執行。","關於"));
        menu.Items.Add(new Separator());
        var quit=new MenuItem{Header="結束程式"};
        quit.Click+=(_,e)=>
        {
            e.Handled=true;
            menu.IsOpen=false;
            // Finish dismissing the tray popup before opening a modal dialog.
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(async()=>
            {
                try {await exit();}
                catch(Exception ex)
                {
                    System.Diagnostics.Trace.TraceError(ex.ToString());
                    Open();
                    MessageBox.Show(this,$"結束程式失敗，請重試。\n{ex.Message}","Cloud Alarm Overlay",MessageBoxButton.OK,MessageBoxImage.Error);
                }
            }));
        };
        menu.Items.Add(quit);
        var logo=new BitmapImage(new Uri("pack://application:,,,/CloudAlarmOverlay.App;component/Assets/Brand/cloud_alarm_icon.png"));
        tray=new TaskbarIcon{ToolTipText="Cloud Alarm Overlay",IconSource=logo,ContextMenu=menu,DoubleClickCommand=new RelayCommand(Open)};
        refreshTimer.Tick+=(_,_)=>OnDataChanged();
        refreshTimer.Start();
        countdownTimer.Tick+=(_,_)=>{if(IsVisible)vm.UpdateCountdown();};
        countdownTimer.Start();
    }
    private bool loginOpen;
    private double resizeRatio;
    [StructLayout(LayoutKind.Sequential)]
    private struct SizingRect { public int Left,Top,Right,Bottom; }
    private IntPtr WindowSizing(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x0231)resizeRatio=ActualWidth/ActualHeight; // WM_ENTERSIZEMOVE
        if(message!=0x0214||!vm.Preferences.Maintenance.KeepWindowAspectRatio||WindowState!=WindowState.Normal||resizeRatio<=0)return IntPtr.Zero;
        var rect=Marshal.PtrToStructure<SizingRect>(lParam);
        var edge=wParam.ToInt32();
        var dpi=VisualTreeHelper.GetDpi(this);
        var minWidth=MinWidth*dpi.DpiScaleX;
        var minHeight=MinHeight*dpi.DpiScaleY;
        double width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        // Top/bottom edges drive height; side and corner drags drive width.
        if(edge is 3 or 6)width=height*resizeRatio;else height=width/resizeRatio;
        width=Math.Max(width,Math.Max(minWidth,minHeight*resizeRatio));height=width/resizeRatio;
        if(edge is 1 or 4 or 7)rect.Left=rect.Right-(int)Math.Round(width);else rect.Right=rect.Left+(int)Math.Round(width);
        if(edge is 3 or 4 or 5)rect.Top=rect.Bottom-(int)Math.Round(height);else rect.Bottom=rect.Top+(int)Math.Round(height);
        Marshal.StructureToPtr(rect,lParam,false);handled=true;return new IntPtr(1);
    }
    private bool restoringSelection;
    private void TaskSelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(restoringSelection)return;
        vm.SetTaskSelection(((DataGrid)sender).SelectedItems.Cast<TaskRow>().ToArray());
        vm.SelectedTask=((DataGrid)sender).SelectedItem as TaskRow;
    }
    private void RestoreTaskSelection()
    {
        var rows=vm.SelectedTasks.ToArray();
        restoringSelection=true;
        try {TaskGrid.SelectedItems.Clear();foreach(var row in rows)TaskGrid.SelectedItems.Add(row);}
        finally {restoringSelection=false;}
        vm.SelectedTask=TaskGrid.SelectedItem as TaskRow;
    }
    private void ToggleTaskCheck(object sender)
    {
        if(sender is not CheckBox {DataContext:TaskRow row})return;
        if(TaskGrid.SelectedItems.Contains(row))TaskGrid.SelectedItems.Remove(row);
        else TaskGrid.SelectedItems.Add(row);
    }
    private void TaskCheckMouseDown(object sender,MouseButtonEventArgs e)
    {
        e.Handled=true;ToggleTaskCheck(sender);
    }
    private void TaskCheckKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Space)return;
        e.Handled=true;if(!e.IsRepeat)ToggleTaskCheck(sender);
    }
    private void TaskStateMouseDown(object sender,MouseButtonEventArgs e)
    {
        e.Handled=true;
        if(sender is CheckBox {DataContext:TaskRow row}&&vm.ToggleRowCommand.CanExecute(row))vm.ToggleRowCommand.Execute(row);
    }
    private void TaskStateKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Space)return;
        e.Handled=true;
        if(!e.IsRepeat&&sender is CheckBox {DataContext:TaskRow row}&&vm.ToggleRowCommand.CanExecute(row))vm.ToggleRowCommand.Execute(row);
    }
    private void SelectAllTasks(object sender,RoutedEventArgs e)=>TaskGrid.SelectAll();
    private void ClearTaskSelection(object sender,RoutedEventArgs e)=>TaskGrid.UnselectAll();
    private async void Navigate(object sender,RoutedEventArgs e)
    {
        if(sender is not Button { DataContext: NavigationItem item })return;
        if(item.PageIndex<0)await taskBuilderHost.OpenInBrowserAsync();
        else vm.NavigationIndex=item.PageIndex;
    }
    private void Login()
    {
        if(loginOpen)return;
        loginOpen=true;
        try
        {
            Open();
            if(new AdminLoginWindow(authentication){Owner=this}.ShowDialog()==true){vm.Admin.SelectedTab=0;vm.PageIndex=5;}
        }
        finally{loginOpen=false;}
    }
    private void OnInput(object sender,PreProcessInputEventArgs e)
    {
        if(e.StagingItem.Input is MouseEventArgs or KeyboardEventArgs or TextCompositionEventArgs)vm.Admin.Session.Touch();
    }
    private void SessionChanged()
    {
        Dispatcher.BeginInvoke(async ()=>{
            await vm.Preferences.Maintenance.AdminSessionChangedAsync();
            if(!vm.Admin.Session.IsAuthenticated&&vm.PageIndex==5)vm.PageIndex=0;
            await vm.Admin.SessionChangedAsync();
            vm.Status=vm.Admin.Session.IsAuthenticated?"管理者已登入；閒置 15 分鐘後自動登出。":"已登出管理者模式。";
        });
    }
    private void OnDataChanged()
    {
        Dispatcher.BeginInvoke(()=>{
            if(vm.RefreshCommand.CanExecute(null))vm.RefreshCommand.Execute(null);
        });
    }
    public void Open(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    public void ForceClose(){allowClose=true;Close();}
}
