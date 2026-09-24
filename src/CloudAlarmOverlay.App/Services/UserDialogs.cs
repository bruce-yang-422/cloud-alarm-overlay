using System.IO;
using System.Text;
using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Win32;
namespace CloudAlarmOverlay.App.Services;
public interface IUserDialogs
{
    bool Confirm(string message);
    void Edit(AlarmTask? task,bool copy);
    void Export(string contents);
    Task ImportTasksAsync()=>Task.CompletedTask;
    Task ShareCountdownAsync(string id)=>Task.CompletedTask;
    void ExportNamed(string contents,string filename)=>Export(contents);
    Task<string?> OpenSettingsJsonAsync()=>Task.FromResult<string?>(null);
    void ExportSettingsTemplate() { }
}
public sealed class UserDialogs(ITaskService tasks,EmojiLibrary emojis,ITaskSchedulingService scheduling,LocalTaskCsvService csv,CountdownShareService shares):IUserDialogs
{
    public async Task<string?> OpenSettingsJsonAsync()
    {
        var picker=new OpenFileDialog{Filter="JSON 設定檔|*.json",Multiselect=false};
        if(picker.ShowDialog()!=true)return null;
        await using var stream=File.OpenRead(picker.FileName);
        var bytes=new byte[AdminSettingsJson.MaximumBytes+1];
        var count=await stream.ReadAtLeastAsync(bytes,bytes.Length,throwOnEndOfStream:false);
        if(count>AdminSettingsJson.MaximumBytes)throw new ArgumentException("設定檔上限為 64 KB。");
        var offset=count>=3&&bytes[0]==0xef&&bytes[1]==0xbb&&bytes[2]==0xbf?3:0;
        return new UTF8Encoding(false,true).GetString(bytes,offset,count-offset);
    }
    public void ExportSettingsTemplate()
    {
        var picker=new SaveFileDialog{Filter="JSON 設定檔|*.json",FileName="cao-settings-template.json"};
        if(picker.ShowDialog()==true)File.WriteAllText(picker.FileName,AdminSettingsJson.Template,new UTF8Encoding(false));
    }
    public Task ShareCountdownAsync(string id)=>shares.ShowAsync(id);
    public async Task ImportTasksAsync()
    {
        var picker=new OpenFileDialog{Filter="CSV 檔案|*.csv",Multiselect=false};
        if(picker.ShowDialog()!=true)return;
        if(new FileInfo(picker.FileName).Length>10*1024*1024)throw new InvalidOperationException("CSV 檔案上限為 10 MB。");
        var text=LocalTaskCsvService.Decode(await File.ReadAllBytesAsync(picker.FileName));
        var rows=await csv.PreviewAsync(text);
        new TaskImportWindow{Owner=Application.Current.MainWindow,DataContext=new TaskImportViewModel(csv,rows)}.ShowDialog();
    }
    public bool Confirm(string message)=>MessageBox.Show(message,"Cloud Alarm Overlay",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    public async void Edit(AlarmTask? task,bool copy)
    {
        try { await emojis.LoadAsync(); } catch(Exception ex) { MessageBox.Show(ex.Message,"無法讀取常用 emoji"); return; }
        var vm=new TaskEditorViewModel(tasks,task,copy,scheduling) { Emojis = emojis.Items };
        var window=new TaskEditorWindow{DataContext=vm,Owner=Application.Current.MainWindow};
        vm.Saved+=()=>window.DialogResult=true;
        window.ShowDialog();
    }
    public void ExportNamed(string contents,string filename)
    {
        var picker=new SaveFileDialog{Filter="CSV 檔案|*.csv",FileName=filename};
        if(picker.ShowDialog()==true)File.WriteAllText(picker.FileName,contents,new UTF8Encoding(true));
    }
    public void Export(string contents)
    {
        var picker=new SaveFileDialog{Filter="CSV 檔案|*.csv",FileName=$"提醒紀錄_{DateTime.Now:yyyyMMdd}.csv"};
        if(picker.ShowDialog()==true)File.WriteAllText(picker.FileName,contents,new UTF8Encoding(true));
    }
}
