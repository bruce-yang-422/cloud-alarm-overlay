using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class MainViewModel
{
    [RelayCommand] private async Task ImportTasksAsync()
    {
        try { await dialogs.ImportTasksAsync(); await RefreshAsync(); }
        catch(Exception ex){Status="匯入失敗："+ex.Message;}
    }
    [RelayCommand] private async Task ExportLocalTasksAsync()
    {
        try{dialogs.ExportNamed(LocalTaskCsvService.Export(await tasks.GetAllAsync()),$"本機任務_{DateTime.Now:yyyyMMdd}.csv");}
        catch(Exception ex){Status=ex.Message;}
    }
    [RelayCommand] private void DownloadTaskTemplate()
    {
        try{dialogs.ExportNamed(LocalTaskCsvService.Template(DateTime.Now),"本機任務範本.csv");}
        catch(Exception ex){Status=ex.Message;}
    }
}
