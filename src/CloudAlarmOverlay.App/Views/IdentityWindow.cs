using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.Views;

public partial class IdentityWindow : Window
{
    public IdentityWindow(IDeviceIdentityService identity,IBackupRestoreService? backups=null)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        var viewModel = new IdentityViewModel(identity);
        viewModel.Saved += () => DialogResult = true;
        DataContext = viewModel;
        ImportButton.Visibility=backups is null?Visibility.Collapsed:Visibility.Visible;
        ImportButton.Click+=async(_,_)=>
        {
            if(backups is null)return;
            var picker=new Microsoft.Win32.OpenFileDialog{Filter="Cloud Alarm 備份|*.calbak"};
            if(picker.ShowDialog()!=true)return;
            try
            {
                var preview=await backups.InspectAsync(picker.FileName);
                if(new BackupImportWindow(backups,picker.FileName,preview){Owner=this}.ShowDialog()!=true)return;
                viewModel.DeviceId=preview.DeviceId;viewModel.DisplayName=preview.DisplayName??"";
                viewModel.Error="備份已還原，請確認身分後按開始使用。";
                DeviceCode.IsEnabled=false;DisplayNameInput.IsEnabled=false;
                StartButton.Command=null;StartButton.Click+=(_,_)=>DialogResult=true;
            }
            catch(Exception ex){viewModel.Error=ex.Message;}
        };
    }
}
