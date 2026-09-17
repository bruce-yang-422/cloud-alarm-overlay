using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;
public partial class TaskPreviewWindow:Window
{
    public TaskPreviewWindow(TaskPreviewViewModel model)
    {
        InitializeComponent();DataContext=model;
        MaxHeight=SystemParameters.WorkArea.Height;
        MaxWidth=SystemParameters.WorkArea.Width;
    }
}
