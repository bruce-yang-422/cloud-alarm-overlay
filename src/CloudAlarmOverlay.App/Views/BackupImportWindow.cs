using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.Views;

public sealed class BackupImportWindow : Window
{
    public BackupImportWindow(IBackupRestoreService backups,string path,BackupPreview preview)
    {
        Title="確認還原備份"; Width=520; SizeToContent=SizeToContent.Height; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/CloudAlarmOverlay.App;component/Styles/LightTheme.xaml",UriKind.Relative)});
        var panel=new StackPanel{Margin=new Thickness(24)};Content=panel;
        panel.Children.Add(new TextBlock{Text=$"{preview.DeviceId} · {preview.DisplayName}",FontSize=20,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text=$"任務 {preview.Tasks} 筆，歷史 {preview.History} 筆。\n個人設定及身分會還原，重複任務保留本機版本。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
        var name=new TextBox{Text="admin"};var password=new PasswordBox{Padding=new Thickness(10)};
        if(preview.ContainsAdministrator)
        {
            panel.Children.Add(new TextBlock{Text="此備份會取代管理者帳密。請輸入目前管理者帳密以確認。",TextWrapping=TextWrapping.Wrap});
            panel.Children.Add(name);panel.Children.Add(password);
        }
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};panel.Children.Add(error);
        var confirm=new Button{Content="確認還原",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};panel.Children.Add(confirm);
        confirm.Click+=async(_,_)=>
        {
            confirm.IsEnabled=false;
            try {await backups.RestoreAsync(path,preview.ContainsAdministrator,preview.ContainsAdministrator?new(name.Text,password.Password):null);DialogResult=true;}
            catch(Exception ex){error.Text=ex.Message;}
            finally{password.Clear();confirm.IsEnabled=true;}
        };
    }
}
