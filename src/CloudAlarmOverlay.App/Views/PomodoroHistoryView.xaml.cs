using System.Windows.Controls;
namespace CloudAlarmOverlay.App.Views;
public partial class PomodoroHistoryView:UserControl
{
    public PomodoroHistoryView(){InitializeComponent();SizeChanged+=(_,_)=>HeaderScroll.MaxHeight=System.Math.Max(160,ActualHeight-180);}
}