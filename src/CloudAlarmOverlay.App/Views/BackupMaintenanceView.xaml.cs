using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Win32;
namespace CloudAlarmOverlay.App.Views;
public partial class BackupMaintenanceView:UserControl
{
    public BackupMaintenanceView()=>InitializeComponent();
    private async void ExportBackup(object sender,RoutedEventArgs e)
    {
        if(DataContext is not MaintenanceViewModel vm||vm.Busy)return;
        try
        {
            vm.RequireAdministrator();
            var file=new SaveFileDialog{Filter="Cloud Alarm 備份|*.calbak",FileName=$"backup_{DateTime.Now:yyyyMMdd}.calbak"};
            if(file.ShowDialog(Window.GetWindow(this))!=true)return;
            vm.Busy=true;IsEnabled=false;vm.RequireAdministrator();
            async Task Create(BackupCredentials? credentials)
            {
                vm.RequireAdministrator();await vm.Backups.CreateBackupAsync(file.FileName,credentials);vm.Message="備份已建立。";
            }
            if(IncludeAdmin.IsChecked==true&&vm.IsCompanyMode)
                new AdminCredentialsDialog("backup","",vm.RequireAdministrator,input=>Create(new(input.Username,input.CurrentPassword))){Owner=Window.GetWindow(this)}.ShowDialog();
            else await Create(null);
        }
        catch(Exception ex){vm.Message="備份失敗："+ex.Message;}
        finally{vm.Busy=false;IsEnabled=true;}
    }
    private async void ImportBackup(object sender,RoutedEventArgs e)
    {
        if(DataContext is not MaintenanceViewModel vm||vm.Busy)return;
        try
        {
            vm.RequireAdministrator();
            var file=new OpenFileDialog{Filter="Cloud Alarm 備份|*.calbak"};
            if(file.ShowDialog(Window.GetWindow(this))!=true)return;
            vm.Busy=true;IsEnabled=false;vm.RequireAdministrator();
            var preview=await vm.Backups.InspectAsync(file.FileName);
            var text=$"裝置：{preview.DeviceId}／{preview.DisplayName}\n任務 {preview.Tasks} 筆、歷史 {preview.History} 筆、倒數 {preview.Countdowns} 筆。\n\n覆寫：裝置身分及未鎖定的個人設定。\n合併：任務、歷史與倒數；重複項目保留本機版本。\n\n首頁釘選依 2–5 項上限還原，現有釘選優先；超額項目仍匯入但不釘選。";
            if(preview.ContainsAdministrator)text+=vm.IsCompanyMode?"\n\n此備份包含管理帳密，會取代目前帳密；下一步需驗證目前管理者。":"\n\n此備份包含管理帳密；還原後仍維持個人使用模式。";
            if(new GoogleActionDialog("預覽備份還原","本機資料",text,"繼續還原"){Owner=Window.GetWindow(this)}.ShowDialog()!=true)return;
            async Task Restore(BackupCredentials? credentials)
            {
                vm.RequireAdministrator();
                var result=await vm.Backups.RestoreAsync(file.FileName,preview.ContainsAdministrator,credentials);
                await vm.InitializeAsync();
                vm.Message=$"已匯入 {result.ImportedTasks} 筆任務（略過 {result.SkippedTasks} 筆重複）、{result.ImportedHistory} 筆歷史、{result.ImportedCountdowns} 筆倒數（略過 {result.SkippedCountdowns} 筆重複）。請重新啟動以更新全部畫面。";
            }
            if(preview.ContainsAdministrator&&vm.IsCompanyMode)
                new AdminCredentialsDialog("backup","",vm.RequireAdministrator,input=>Restore(new(input.Username,input.CurrentPassword))){Owner=Window.GetWindow(this)}.ShowDialog();
            else await Restore(null);
        }
        catch(Exception ex){vm.Message="還原失敗："+ex.Message;}
        finally{vm.Busy=false;IsEnabled=true;}
    }
}
