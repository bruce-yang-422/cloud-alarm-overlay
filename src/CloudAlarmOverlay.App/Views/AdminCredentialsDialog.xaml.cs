using System.Windows;
using System.Windows.Threading;
namespace CloudAlarmOverlay.App.Views;
public sealed record AdminCredentialInput(string Username,string CurrentPassword,string NewPassword);
public partial class AdminCredentialsDialog:Window
{
    private readonly string purpose;
    private readonly Action authorize;
    private readonly Func<AdminCredentialInput,Task> apply;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private bool busy;
    public AdminCredentialsDialog(string purpose,string username,Action authorize,Func<AdminCredentialInput,Task> apply)
    {
        InitializeComponent();this.purpose=purpose;this.authorize=authorize;this.apply=apply;
        Username.Text=username;
        Title=Heading.Text=purpose switch{"exit"=>"設定專用結束密碼","backup"=>"驗證管理者帳密",_=>"修改管理者帳號與密碼"};
        Description.Text=purpose switch{"exit"=>"設定 8–128 字的專用密碼，結束程式時使用。","backup"=>"備份或還原管理帳密前，請驗證目前的管理者。",_=>"新密碼留空可只修改帳號。儲存後需重新登入。"};
        UsernameField.Visibility=CurrentField.Visibility=purpose=="exit"?Visibility.Collapsed:Visibility.Visible;
        NewFields.Visibility=purpose=="backup"?Visibility.Collapsed:Visibility.Visible;
        ApplyButton.Content=purpose=="backup"?"驗證並繼續":"儲存";
        timer.Tick+=(_,_)=>
        {
            try{authorize();}
            catch(UnauthorizedAccessException){ClearPasswords();Fields.IsEnabled=false;ApplyButton.IsEnabled=false;Error.Text="登入已逾時，請關閉後重新登入管理者。";timer.Stop();}
        };
        Loaded+=(_,_)=>{timer.Start();CancelButton.Focus();};
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        Closed+=(_,_)=>{timer.Stop();ClearPasswords();};
    }
    private void ClearPasswords(){CurrentPassword.Clear();NewPassword.Clear();ConfirmPassword.Clear();}
    private async void Apply(object sender,RoutedEventArgs e)
    {
        if(busy)return;busy=true;ApplyButton.IsEnabled=Fields.IsEnabled=CancelButton.IsEnabled=false;
        try
        {
            authorize();
            if(purpose!="exit"&&(string.IsNullOrWhiteSpace(Username.Text)||CurrentPassword.Password.Length==0))throw new ArgumentException("請輸入管理者帳號及目前密碼。");
            if(purpose!="backup"&&NewPassword.Password!=ConfirmPassword.Password)throw new ArgumentException("兩次輸入的新密碼不一致。");
            if(purpose=="exit"&&NewPassword.Password.Length<8)throw new ArgumentException("結束密碼至少需要 8 字。");
            await apply(new(Username.Text.Trim(),CurrentPassword.Password,NewPassword.Password));
            busy=false;DialogResult=true;
        }
        catch(Exception ex){Error.Text=ex.Message;ClearPasswords();}
        finally{busy=false;ApplyButton.IsEnabled=Fields.IsEnabled=CancelButton.IsEnabled=true;}
    }
}
