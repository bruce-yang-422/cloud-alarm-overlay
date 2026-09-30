using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;
public enum TaskPreviewAction{None,Edit,Copy}
public partial class TaskPreviewWindow:Window
{
    // Chosen follow-up action; the caller runs it after the preview closes so editors never stack over the preview.
    public TaskPreviewAction Action{get;private set;}
    public TaskPreviewWindow(TaskPreviewViewModel model,string? editLabel=null,bool canCopy=false)
    {
        InitializeComponent();DataContext=model;
        MaxHeight=SystemParameters.WorkArea.Height;
        MaxWidth=SystemParameters.WorkArea.Width;
        if(editLabel is not null){EditButton.Content=editLabel;EditButton.Visibility=Visibility.Visible;}
        if(canCopy){CopyButton.Visibility=Visibility.Visible;CopyButton.Margin=new(editLabel is null?0:10,0,0,0);}
    }
    private void OnEdit(object sender,RoutedEventArgs e){Action=TaskPreviewAction.Edit;Close();}
    private void OnCopy(object sender,RoutedEventArgs e){Action=TaskPreviewAction.Copy;Close();}
}
