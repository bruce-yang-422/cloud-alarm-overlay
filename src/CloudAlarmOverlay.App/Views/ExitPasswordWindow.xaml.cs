using System.Windows;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.Views;

public partial class ExitPasswordWindow:Window
{
    private readonly IExitProtectionService protection;
    private bool busy;
    public ExitPasswordWindow(IExitProtectionService protection)
    {
        this.protection=protection;
        InitializeComponent();
        Loaded+=(_,_)=>Password.Focus();
    }
    private void OnPasswordChanged(object sender,RoutedEventArgs e)=>Submit.IsEnabled=!busy&&Password.Password.Length>0;
    private async void OnSubmit(object sender,RoutedEventArgs e)
    {
        if(busy)return;
        busy=true;Submit.IsEnabled=false;Error.Text="";
        try
        {
            if(await protection.VerifyDedicatedPasswordAsync(Password.Password))DialogResult=true;
            else{Error.Text="結束程式密碼錯誤。";Password.Clear();Password.Focus();}
        }
        catch(Exception ex){Error.Text=ex.Message;Password.Clear();}
        finally{busy=false;Submit.IsEnabled=Password.Password.Length>0;}
    }
}
