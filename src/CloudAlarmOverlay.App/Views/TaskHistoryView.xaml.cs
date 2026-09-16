using System.Windows.Controls;
namespace CloudAlarmOverlay.App.Views;
public partial class TaskHistoryView:UserControl
{
    public TaskHistoryView(){InitializeComponent();SizeChanged+=(_,_)=>HeaderScroll.MaxHeight=System.Math.Max(160,ActualHeight-180);}
}