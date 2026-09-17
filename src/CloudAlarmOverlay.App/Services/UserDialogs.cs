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
    void ExportNamed(string contents,string filename)=>Export(contents);
}
public sealed class UserDialogs(ITaskService tasks,EmojiLibrary emojis,ITaskSchedulingService scheduling):IUserDialogs
{
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
