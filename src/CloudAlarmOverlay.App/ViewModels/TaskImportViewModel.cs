using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class TaskImportSelection(TaskImportRow row) : ObservableObject
{
    public TaskImportRow Row {get;}=row;
    [ObservableProperty] private bool selected=row.Valid&&!row.Duplicate;
}
public partial class TaskImportViewModel(LocalTaskCsvService service,IReadOnlyList<TaskImportRow> rows) : ObservableObject
{
    public ObservableCollection<TaskImportSelection> Rows {get;}=new(rows.Select(r=>new TaskImportSelection(r)));
    [ObservableProperty] private bool useNewIds;
    [ObservableProperty] private bool finished;
    [ObservableProperty] private string message=$"共 {rows.Count} 筆，錯誤 {rows.Count(r=>!r.Valid)} 筆，重複編號 {rows.Count(r=>r.Duplicate)} 筆。請勾選要匯入的列。";
    public bool CanImport=>!Finished;
    partial void OnFinishedChanged(bool value){OnPropertyChanged(nameof(CanImport));ImportCommand.NotifyCanExecuteChanged();}
    [RelayCommand] private void SelectValid(){foreach(var row in Rows)row.Selected=row.Row.Valid;}
    [RelayCommand(CanExecute=nameof(CanImport))] private async Task ImportAsync()
    {
        try
        {
            var selected=Rows.Where(r=>r.Selected).Select(r=>r.Row).ToArray();
            var result=await service.ImportAsync(selected,UseNewIds);
            Message=$"成功匯入 {result.Imported} 筆，略過 {result.Skipped+Rows.Count-selected.Length} 筆。";
            Finished=true;
        }
        catch(Exception ex){Message="匯入中止："+ex.Message+"；已建立的任務會保留，重試時既有編號不會覆蓋。";}
    }
}
