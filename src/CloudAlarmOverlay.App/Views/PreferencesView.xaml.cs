using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;
public partial class PreferencesView:UserControl
{
    private PreferencesViewModel? vm;
    private bool navigationOpen,narrow;
    private readonly HashSet<QuietPeriodRow> watchedRows=[];
    public PreferencesView()=>InitializeComponent();
    private void WorkspaceLoaded(object sender,RoutedEventArgs e)
    {
        if(vm is null&&DataContext is PreferencesViewModel model)
        {
            vm=model;vm.CanLeavePage=CanLeave;
            if(Window.GetWindow(this)?.DataContext is MainViewModel main)main.CanLeaveSettings=CanLeave;
            vm.PropertyChanged+=Changed;vm.DraftsChanged+=UpdateFooter;
            vm.EmojiItems.CollectionChanged+=(_,_)=>UpdateFooter();
            vm.QuietPeriods.CollectionChanged+=(_,_)=>{WatchQuietRows();UpdateFooter();};WatchQuietRows();
            if(vm.Weather is {} weather)weather.PropertyChanged+=(_,_)=>UpdateFooter();
        }
        UpdateFooter();ApplyNavigationLayout();
    }
    private void WatchQuietRows()
    {
        if(vm is null)return;
        foreach(var row in watchedRows.Where(r=>!vm.QuietPeriods.Contains(r)).ToArray()){row.PropertyChanged-=QuietChanged;watchedRows.Remove(row);}
        foreach(var row in vm.QuietPeriods)if(watchedRows.Add(row))row.PropertyChanged+=QuietChanged;
    }
    private void QuietChanged(object? sender,PropertyChangedEventArgs e)=>UpdateFooter();
    private void Changed(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(PreferencesViewModel.SelectedPage))
        {
            navigationOpen=false;ApplyNavigationLayout();OperationResult.Text="";
            NavigationList.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.UpdateTarget();
        }
        if(e.PropertyName==nameof(PreferencesViewModel.Message))OperationResult.Text=vm?.Message??"";
        if(e.PropertyName==nameof(PreferencesViewModel.EmojiMessage)&&vm?.SelectedPage==SettingsPage.Editing)OperationResult.Text=vm.EmojiMessage;
        UpdateFooter();
    }
    private bool CanLeave()
    {
        if(vm is null)return true;
        if(vm.IsSaving)return false;
        if(vm.SelectedPage==SettingsPage.Google)return GooglePage.CanLeave();
        if(!vm.IsPageDirty(vm.SelectedPage)&&!HasValidationError(SettingsTabs))return true;
        if(!vm.ConfirmDiscardChanges())return false;
        vm.DiscardPage(vm.SelectedPage);ResetInvalidInputs(SettingsTabs);return true;
    }
    private void ValidationChanged(object sender,ValidationErrorEventArgs e)=>Dispatcher.BeginInvoke(UpdateFooter);
    private static void ResetInvalidInputs(DependencyObject node)
    {
        if(node is TextBox box&&Validation.GetHasError(box))box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        if(node is ComboBox combo&&Validation.GetHasError(combo))combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateTarget();
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)ResetInvalidInputs(System.Windows.Media.VisualTreeHelper.GetChild(node,i));
    }
    private void UpdateFooter()
    {
        if(vm is null)return;
        DraftFooter.Visibility=vm.IsPageDirty(vm.SelectedPage)||HasValidationError(SettingsTabs)?Visibility.Visible:Visibility.Collapsed;
        WorkspaceGrid.IsEnabled=!vm.IsSaving;
    }
    private async void SaveDraft(object sender,RoutedEventArgs e)
    {
        if(vm is null||HasValidationError(SettingsTabs))return;
        await vm.SavePageAsync();UpdateFooter();
    }
    private static bool HasValidationError(DependencyObject root)
    {
        if(Validation.GetHasError(root))return true;
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
            if(HasValidationError(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))return true;
        return false;
    }
    private void DiscardDraft(object sender,RoutedEventArgs e)
    {
        if(vm is not null&&vm.ConfirmDiscardChanges()){vm.DiscardPage(vm.SelectedPage);ResetInvalidInputs(SettingsTabs);OperationResult.Text="已捨棄尚未儲存的變更。";UpdateFooter();}
    }
    private void ResetEmojiDefaults(object sender,RoutedEventArgs e)
    {
        if(vm is not null&&vm.ConfirmResetEmojis())vm.ResetEmojisCommand.Execute(null);
    }
    private void OpenUpdates(object sender,RoutedEventArgs e){if(vm is not null)vm.SelectedPage=SettingsPage.Updates;}
    private void PreviewReminder(object sender,RoutedEventArgs e)
    {
        if(sender is not Button button||Window.GetWindow(this)?.DataContext is not MainViewModel main)return;
        var menu=new ContextMenu();
        foreach(var level in new[]{"一般提醒","重要提醒","緊急提醒","強制通知"})menu.Items.Add(new MenuItem{Header=level,Command=main.PreviewCommand,CommandParameter=level});
        menu.PlacementTarget=button;menu.IsOpen=true;
    }
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
    }
    private void OpenWindowsEmoji(object sender, System.Windows.RoutedEventArgs e) => CloudAlarmOverlay.App.Services.WindowsEmojiPanel.Open(NewEmojiInput);
}
