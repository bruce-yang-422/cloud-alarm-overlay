using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;
public partial class CalendarDataView:UserControl
{
    public CalendarDataView(){InitializeComponent();Loaded+=async(_,_)=>{if(DataContext is CalendarDataViewModel vm)await vm.OpenAsync();};}
}
