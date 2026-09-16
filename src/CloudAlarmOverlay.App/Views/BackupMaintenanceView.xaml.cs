using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Win32;
namespace CloudAlarmOverlay.App.Views;

public partial class BackupMaintenanceView : UserControl
{
    public BackupMaintenanceView()=>InitializeComponent();
    private BackupCredentials Credentials()=>new(AdminName.Text,AdminPassword.Password);
    private async void ChangePassword(object sender,RoutedEventArgs e)
    {
        if(DataContext is not MaintenanceViewModel vm||vm.Busy)return;
        vm.Busy=true;IsEnabled=false;
        try
        {
            vm.RequireAdministrator();
            if(NewPassword.Password!=ConfirmPassword.Password)throw new ArgumentException("兩次新密碼不一致。");
            await vm.ChangePasswordAsync(AdminName.Text,AdminPassword.Password,NewPassword.Password);
            vm.Message="管理者密碼已更新，請以新密碼重新登入。";
        }
        catch(Exception ex){vm.Message=ex.Message;}
        finally{AdminPassword.Clear();NewPassword.Clear();ConfirmPassword.Clear();vm.Busy=false;IsEnabled=true;}
    }
    private async void ExportBackup(object sender,RoutedEventArgs e)
    {
        if(DataContext is not MaintenanceViewModel vm||vm.Busy)return;
        var file=new SaveFileDialog{Filter="Cloud Alarm 備份|*.calbak",FileName=$"backup_{DateTime.Now:yyyyMMdd}.calbak"};
        if(file.ShowDialog()!=true)return;
        vm.Busy=true; IsEnabled=false;
        try {vm.RequireAdministrator();await vm.Backups.CreateBackupAsync(file.FileName,IncludeAdmin.IsChecked==true?Credentials():null); vm.Message="備份已建立。";}
        catch(Exception ex){vm.Message="備份失敗："+ex.Message;}
        finally {AdminPassword.Clear();vm.Busy=false;IsEnabled=true;}
    }
    private async void ImportBackup(object sender,RoutedEventArgs e)
    {
        if(DataContext is not MaintenanceViewModel vm||vm.Busy)return;
        var file=new OpenFileDialog{Filter="Cloud Alarm 備份|*.calbak"}; if(file.ShowDialog()!=true)return;
        vm.Busy=true; IsEnabled=false;
        try
        {
            vm.RequireAdministrator();
            var preview=await vm.Backups.InspectAsync(file.FileName);
            var text=$"還原裝置：{preview.DeviceId}／{preview.DisplayName}\n任務 {preview.Tasks} 筆，歷史 {preview.History} 筆。\n裝置身分及未鎖定的個人設定會覆蓋，重複任務與歷史保留本機版本。";
            if(preview.ContainsAdministrator)text+="\n此備份含管理者帳密，會取代目前帳密；請先在此頁填入目前帳密驗證。";
            if(MessageBox.Show(text,"確認還原備份",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
            vm.RequireAdministrator();
            var result=await vm.Backups.RestoreAsync(file.FileName,preview.ContainsAdministrator,preview.ContainsAdministrator?Credentials():null);
            await vm.InitializeAsync();
            vm.Message=$"已匯入 {result.ImportedTasks} 筆任務，略過 {result.SkippedTasks} 筆重複，匯入 {result.ImportedHistory} 筆歷史。請重新啟動以更新全部畫面。";
        }
        catch(Exception ex){vm.Message="還原失敗："+ex.Message;}
        finally {AdminPassword.Clear();vm.Busy=false;IsEnabled=true;}
    }
}
