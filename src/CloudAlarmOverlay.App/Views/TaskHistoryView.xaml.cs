using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;
public partial class TaskHistoryView:UserControl
{
    public TaskHistoryView(){InitializeComponent();SizeChanged+=(_,_)=>HeaderScroll.MaxHeight=System.Math.Max(160,ActualHeight-180);}
    private void HistoryTitleDoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount!=2 || (sender as FrameworkElement)?.DataContext is not HistoryRow row)return;
        e.Handled=true;
        new TaskPreviewWindow(TaskPreviewViewModel.FromHistory(row.Entry)){Owner=Window.GetWindow(this)}.ShowDialog();
    }
}