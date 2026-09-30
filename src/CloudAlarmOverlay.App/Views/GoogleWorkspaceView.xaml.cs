using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.App.Views;

public partial class GoogleWorkspaceView:UserControl
{
    private bool dialogOpen;
    private GoogleWorkspaceViewModel? observed;
    private readonly System.Windows.Threading.DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    public GoogleWorkspaceView()
    {
        InitializeComponent();
        Loaded+=async(_,_)=>
        {
            if(DataContext is GoogleWorkspaceViewModel vm)
            {
                if(observed!=vm){if(observed is not null)observed.PropertyChanged-=SelectionUpdated;observed=vm;vm.PropertyChanged+=SelectionUpdated;}
                await vm.LoadAsync();
            }
            refreshTimer.Start();
        };
        Unloaded+=(_,_)=>refreshTimer.Stop();
        refreshTimer.Tick+=async(_,_)=>{if(IsVisible&&!dialogOpen&&DataContext is GoogleWorkspaceViewModel vm&&!vm.Busy)await vm.RefreshStatusCommand.ExecuteAsync(null);};
    }
    private void SelectionUpdated(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(GoogleWorkspaceViewModel.Selected)){SettingsScroll.ScrollToTop();TaskEditorExpander.IsExpanded=false;}
    }
    public bool CanLeave()
    {
        if(DataContext is not GoogleWorkspaceViewModel vm)return true;
        if(vm.Busy){vm.Message="請等待目前操作完成，或先取消操作。";return false;}
        if((vm.HasSourceChanges||Invalid(SettingsScroll))&&!vm.ConfirmDiscardDraft())return false;
        vm.DiscardSource();ResetInvalidInputs(SettingsScroll);return true;
    }
    private void BackToAccounts(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel vm&&CanLeave()){vm.Selected=vm.Tabs[0];SettingsScroll.ScrollToTop();vm.Message="";}
    }
    private void EditSource(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel {CanAct:true,Selected:{} source} vm){source.Locked=false;vm.Message="編輯後請儲存變更。";}
    }
    private void DiscardSource(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel vm&&vm.ConfirmDiscardDraft()){vm.DiscardSource();ResetInvalidInputs(SettingsScroll);vm.Message="已捨棄來源變更。";}
    }
    private async void SaveSource(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel vm&&!Invalid(SettingsScroll))await vm.SaveSourceCommand.ExecuteAsync(null);
    }
    private static void ResetInvalidInputs(DependencyObject root)
    {
        if(root is TextBox box&&Validation.GetHasError(box))box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)ResetInvalidInputs(System.Windows.Media.VisualTreeHelper.GetChild(root,i));
    }
    private static bool Invalid(DependencyObject root)
    {
        if(Validation.GetHasError(root))return true;
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
            if(Invalid(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))return true;
        return false;
    }
    private void SourceMenu(object sender,RoutedEventArgs e)
    {
        if(sender is not Button button)return;
        var menu=new ContextMenu{PlacementTarget=button};
        var remove=new MenuItem{Header="移除來源…"};remove.Click+=RemoveSource;menu.Items.Add(remove);button.ContextMenu=menu;menu.IsOpen=true;
    }
    private Window? taskWindow;
    private void OpenTaskEditor(object sender,RoutedEventArgs e)
    {
        if(DataContext is not GoogleWorkspaceViewModel {CanAct:true,Selected.Locked:true} vm)return;
        var content=TaskEditorExpander.Content;TaskEditorExpander.Content=null;
        var window=new Window{Title="線上任務 · "+vm.Selected!.Name,Owner=Window.GetWindow(this),Width=760,Height=700,MinWidth=580,MinHeight=480,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,DataContext=vm};
        window.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/CloudAlarmOverlay.App;component/Styles/AdminWorkspaceStyles.xaml",UriKind.Relative)});
        window.SetBinding(Window.BackgroundProperty,new System.Windows.Data.Binding("Background"){Source=(Grid)Content});
        window.SetBinding(Window.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){Source=this});
        var panel=new DockPanel{Margin=new Thickness(24)};
        var result=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
        result.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Message"));DockPanel.SetDock(result,Dock.Bottom);panel.Children.Add(result);
        panel.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});window.Content=panel;
        window.Closing+=(_,args)=>{if(vm.Busy||(vm.HasTaskEdits&&!vm.ConfirmDiscardDraft()))args.Cancel=true;};
        taskWindow=window;dialogOpen=true;
        try{window.ShowDialog();}
        finally{((ScrollViewer)panel.Children[1]).Content=null;TaskEditorExpander.Content=content;vm.ClearTaskEditor();dialogOpen=false;taskWindow=null;}
    }
    private void SourceChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.OriginalSource))return;
        SettingsScroll?.ScrollToTop();
        if(TaskEditorExpander is not null)TaskEditorExpander.IsExpanded=false;
    }
    // Owned modal windows prevent account/source selection from changing during confirmation.
    private bool ShowDialog(Window dialog)
    {
        dialog.Owner=taskWindow??Window.GetWindow(this);
        var wasOpen=dialogOpen;
        dialogOpen=true;
        try{return dialog.ShowDialog()==true;}
        finally{dialogOpen=wasOpen;}
    }
    private bool Confirm(string title,string subject,string description,string action,bool destructive=false)=>
        ShowDialog(new GoogleActionDialog(title,subject,description,action,destructive));
    private async void AddAccount(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel {CanAct:true,CanSignIn:true} vm && ShowDialog(new GoogleSignInDialog(vm)))
            await vm.SignInCommand.ExecuteAsync(null);
    }
    private void AddAccountSource(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel {CanAct:true} vm && sender is FrameworkElement {DataContext:GoogleAccount account})
        {vm.SelectedAccount=account;vm.AddSourceCommand.Execute(null);}
    }
    private void OpenSource(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel {CanAct:true} vm && sender is FrameworkElement {DataContext:GoogleSourceDraft source}){vm.Selected=source;SettingsScroll.ScrollToTop();vm.Message="";}
    }
    private void AccountMenu(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel {CanAct:true} && sender is Button {ContextMenu:{} menu} button)
        {menu.DataContext=button.DataContext;menu.PlacementTarget=button;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;}
    }
    private async Task WriteAsync(bool delete)
    {
        if(DataContext is not GoogleWorkspaceViewModel {CanAct:true} vm)return;
        try
        {
            if(Confirm(delete?"刪除雲端任務？":"確認寫入線上任務",vm.Selected?.Name??"",vm.PreviewTask(delete),delete?"刪除任務":"確認寫入",delete))
                await vm.WriteTaskAsync(delete);
        }
        catch(Exception ex){vm.Message=ex.Message;}
    }
    private async void WriteTask(object sender,RoutedEventArgs e)=>await WriteAsync(false);
    private async void DeleteTask(object sender,RoutedEventArgs e)=>await WriteAsync(true);
    private async void SignOut(object sender,RoutedEventArgs e)=>await DisconnectAccountAsync(sender,false);
    private async void RenameAccount(object sender,RoutedEventArgs e)
    {
        if(DataContext is not GoogleWorkspaceViewModel {CanAct:true} vm || sender is not FrameworkElement {DataContext:GoogleAccount account})return;
        var dialog=new GoogleAccountNameDialog(account);
        if(ShowDialog(dialog))await vm.RenameAccountAsync(account.Id,dialog.AccountName);
    }
    private async void Revoke(object sender,RoutedEventArgs e)=>await DisconnectAccountAsync(sender,true);
    private async Task DisconnectAccountAsync(object sender,bool revoke)
    {
        if(DataContext is not GoogleWorkspaceViewModel {CanAct:true} vm || sender is not FrameworkElement {DataContext:GoogleAccount account})return;
        var count=vm.Tabs.Count(t=>t.Id!=""&&t.AccountId==account.Id);
        var description=$"將移除此帳號的 {count} 個本機同步來源與任務快取。Google 試算表與日曆中的資料會保留。";
        if(revoke)description+="\n\n也會撤銷此帳號對本 Google 專案的授權，可能影響使用相同帳號的其他裝置。";
        else description+="\n\n之後可以重新登入，再新增同步來源。";
        if(Confirm(revoke?"解除 Google 授權？":"登出此 Google 帳號？",account.Email,description,revoke?"解除授權":"登出帳號",revoke))
        {vm.SelectedAccount=account;await vm.SignOutAsync(revoke);}
    }
    private async void RemoveSource(object sender,RoutedEventArgs e)
    {
        if(DataContext is GoogleWorkspaceViewModel {CanAct:true,IsSource:true} vm &&
           Confirm("移除同步來源？",vm.Selected!.Name,"此來源將停止同步，並清除其本機任務快取。\n\nGoogle 試算表與日曆中的資料會保留。","移除來源",true))await vm.RemoveSourceAsync();
    }
}
