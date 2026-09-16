using System.Windows.Controls;
using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;
public partial class AdminView:UserControl
{
    public AdminView()=>InitializeComponent();
    private bool exitPasswordBusy;
    private void ToggleAutoStart(object sender,RoutedEventArgs e)
    {
        if(DataContext is MainViewModel vm)vm.Admin.SaveAutoStart();
    }
    private async void ToggleExitPasswordRequirement(object sender,RoutedEventArgs e)
    {
        if(DataContext is MainViewModel vm)await vm.Admin.SaveExitPasswordRequirementAsync();
    }
    private async void SetExitPassword(object sender,RoutedEventArgs e)
    {
        if(exitPasswordBusy||DataContext is not MainViewModel vm)return;
        exitPasswordBusy=true;
        try
        {
            if(ExitPassword.Password!=ExitPasswordConfirm.Password)throw new ArgumentException("兩次輸入的結束密碼不一致。");
            await vm.Admin.SetExitPasswordAsync(ExitPassword.Password);
        }
        catch(Exception ex){vm.Admin.Message=ex.Message;}
        finally{ExitPassword.Clear();ExitPasswordConfirm.Clear();exitPasswordBusy=false;}
    }
    private async void UseAdminExitPassword(object sender,RoutedEventArgs e)
    {
        if(exitPasswordBusy||DataContext is not MainViewModel vm)return;
        exitPasswordBusy=true;
        try{await vm.Admin.UseAdministratorExitPasswordAsync();}
        catch(Exception ex){vm.Admin.Message=ex.Message;}
        finally{ExitPassword.Clear();ExitPasswordConfirm.Clear();exitPasswordBusy=false;}
    }
}
