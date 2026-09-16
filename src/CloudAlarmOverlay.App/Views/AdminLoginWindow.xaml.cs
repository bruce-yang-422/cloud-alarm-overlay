using System.Windows;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.Views;
public partial class AdminLoginWindow:Window
{
    private readonly IAuthenticationService authentication;
    private readonly bool forExit;
    private bool setup,busy;
    public AdminLoginWindow(IAuthenticationService authentication,bool forExit=false)
    {
        this.authentication=authentication;this.forExit=forExit;
        InitializeComponent();
        MaxHeight=SystemParameters.WorkArea.Height;
        Loaded+=async(_,_)=>{
            try
            {
                setup=!await authentication.HasAdministratorAsync();
                if(setup && forExit)
                {
                    Title="驗證管理者以結束程式";
                    Heading.Text="結束程式驗證";
                    Help.Text="請輸入管理者帳號與密碼。取消或驗證失敗時，提醒會繼續運作。";
                    Error.Text="尚未建立管理者帳號，無法驗證結束程式。";
                    Submit.Content="驗證並結束";
                }
                else if(setup){Heading.Text="建立這台電腦的管理者";Help.Text="首次設定請由 IT 操作。設定獨立帳號與 8–128 字密碼，沒有預設密碼。";ConfirmPanel.Visibility=Visibility.Visible;Submit.Content="建立並登入";}
                else
                {
                    Username.Text="admin";
                    if(forExit)
                    {
                        Title="驗證管理者以結束程式";
                        Heading.Text="結束程式驗證";
                        Help.Text="請輸入管理者帳號與密碼。取消或驗證失敗時，提醒會繼續運作。";
                        Submit.Content="驗證並結束";
                    }
                }
                Username.Focus();UpdateSubmit();
            }
            catch(Exception ex){Error.Text=ex.Message;}
        };
    }
    private void OnInputChanged(object sender,RoutedEventArgs e)=>UpdateSubmit();
    private void UpdateSubmit()
    {
        if(Submit is null||Password is null||Confirmation is null)return;
        Submit.IsEnabled=!busy&&!(forExit&&setup)&&!string.IsNullOrWhiteSpace(Username.Text)&&Password.Password.Length>0&&(!setup||Confirmation.Password.Length>0);
    }
    private async void OnSubmit(object sender,RoutedEventArgs e)
    {
        busy=true;UpdateSubmit();Error.Text="";
        try
        {
            if(setup)
            {
                if(Password.Password!=Confirmation.Password)throw new ArgumentException("兩次輸入的密碼不一致。");
                await authentication.CreateInitialAsync(Username.Text,Password.Password);
                setup=false;ConfirmPanel.Visibility=Visibility.Collapsed;
            }
            if(await authentication.AuthenticateAsync(Username.Text,Password.Password))DialogResult=true;
            else{Error.Text="帳號或密碼錯誤";Password.Clear();Password.Focus();}
        }
        catch(Exception ex){Error.Text=ex.Message;Password.Clear();}
        finally{busy=false;UpdateSubmit();}
    }
}
