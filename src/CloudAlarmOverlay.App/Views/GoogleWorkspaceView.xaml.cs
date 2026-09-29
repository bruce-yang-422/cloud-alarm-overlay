using System.IO;
using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
using Microsoft.Win32;
namespace CloudAlarmOverlay.App.Views;

public partial class GoogleWorkspaceView:UserControl
{
    private readonly System.Windows.Threading.DispatcherTimer refreshTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    public GoogleWorkspaceView()
    {
        InitializeComponent();
        Loaded+=async(_,_)=>{if(DataContext is GoogleWorkspaceViewModel vm)await vm.LoadAsync();refreshTimer.Start();};
        Unloaded+=(_,_)=>refreshTimer.Stop();
        refreshTimer.Tick+=async(_,_)=>{if(IsVisible&&DataContext is GoogleWorkspaceViewModel vm&&!vm.Busy)await vm.RefreshStatusCommand.ExecuteAsync(null);};
    }
    private async void ImportClient(object sender,RoutedEventArgs e)
    {
        if(DataContext is not GoogleWorkspaceViewModel vm)return;
        var dialog=new OpenFileDialog{Filter="Google OAuth JSON|*.json",Title="匯入 Google 桌面 OAuth 設定"};
        if(dialog.ShowDialog()!=true)return;
        try
        {
            if(new FileInfo(dialog.FileName).Length>64*1024)throw new InvalidDataException("設定檔大小超過限制。");
            await vm.ImportAsync(await File.ReadAllTextAsync(dialog.FileName));
        }
        catch(Exception){vm.Message="無法讀取 OAuth 設定檔，請確認為桌面應用程式 JSON。";}
    }
    private bool Confirm(string message)=>MessageBox.Show(Window.GetWindow(this),message,"確認 Google 來源操作",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK;
    private async Task WriteAsync(bool delete)
    {
        if(DataContext is not GoogleWorkspaceViewModel vm)return;
        try{if(Confirm(vm.PreviewTask(delete)))await vm.WriteTaskAsync(delete);}
        catch(Exception ex){vm.Message=ex.Message;}
    }
    private async void WriteTask(object sender,RoutedEventArgs e)=>await WriteAsync(false);
    private async void DeleteTask(object sender,RoutedEventArgs e)=>await WriteAsync(true);
    private async void SignOut(object sender,RoutedEventArgs e){if(DataContext is GoogleWorkspaceViewModel vm&&Confirm("登出選取帳號，並清除該帳號的來源與本機任務快取？"))await vm.SignOutAsync(false);}
    private async void Revoke(object sender,RoutedEventArgs e){if(DataContext is GoogleWorkspaceViewModel vm&&Confirm("撤銷此帳號對本 Google 專案的授權（可能影響同帳號其他裝置），並清除本機來源？"))await vm.SignOutAsync(true);}
    private async void RemoveSource(object sender,RoutedEventArgs e){if(DataContext is GoogleWorkspaceViewModel vm&&Confirm("移除此來源及其本機任務快取？雲端資料不會刪除。"))await vm.RemoveSourceAsync();}
    private void OpenGuide(object sender,RoutedEventArgs e)
    {
        MessageBox.Show(Window.GetWindow(this),"1. 開啟 Google Cloud Console 建立專案。\n2. 啟用 Google Sheets API 與 Google Calendar API。\n3. 在 Google Auth Platform 設定同意畫面與測試使用者。\n4. 建立 OAuth 用戶端，類型選「桌面應用程式」。\n5. 下載 JSON，回此頁按「匯入桌面 OAuth 設定」。\n\n外部 Testing 狀態下，Sheets／日曆更新憑證通常 7 天到期；正式使用前須完成發布設定及所需驗證。服務帳戶金鑰不適用。","Google 登入設定教學");
    }
}
