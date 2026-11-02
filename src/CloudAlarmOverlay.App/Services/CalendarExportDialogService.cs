using System.IO;
using System.Text;
using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Win32;

namespace CloudAlarmOverlay.App.Services;

public interface IIcsFileSaver
{
    Task<bool> SaveAsync(string content,string filename);
}
public sealed class IcsFileSaver:IIcsFileSaver
{
    public async Task<bool> SaveAsync(string content,string filename)
    {
        var picker=new SaveFileDialog{Filter="行事曆檔案 (*.ics)|*.ics",FileName=filename,DefaultExt=".ics",AddExtension=true,OverwritePrompt=true};
        if(picker.ShowDialog()!=true)return false;
        var path=Path.GetFullPath(picker.FileName);
        var temporary=Path.Combine(Path.GetDirectoryName(path)!,"."+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary,content,new UTF8Encoding(false));
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        return true;
    }
}
public sealed class CalendarExportDialogService(ICalendarIcsExporter exporter,IIcsFileSaver files,TimeProvider clock)
{
    public Task ShowAsync(bool includeTasks,bool includeCountdowns)
    {
        var vm=new CalendarExportViewModel(exporter,files,clock,includeTasks,includeCountdowns);
        new CalendarExportWindow(vm){Owner=Application.Current?.MainWindow}.ShowDialog();
        return Task.CompletedTask;
    }
}
