using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CloudAlarmOverlay.App.ViewModels;

namespace CloudAlarmOverlay.App.Views;

public partial class CalendarExportWindow:Window
{
    public CalendarExportWindow(CalendarExportViewModel vm)
    {
        InitializeComponent();DataContext=vm;
        Closing+=(_,_)=>vm.Cancel();
        SizeChanged+=(_,_)=>CloseCalendars();
        LocationChanged+=(_,_)=>CloseCalendars();
    }

    private void CloseCalendars()
    {
        FromDate.IsDropDownOpen=false;
        ToDate.IsDropDownOpen=false;
    }

    private void DateCalendarOpened(object sender,RoutedEventArgs e)
    {
        if(sender is not DatePicker picker||picker.Template.FindName("PART_Popup",picker) is not Popup popup)return;
        popup.PlacementTarget=picker;
        popup.CustomPopupPlacementCallback=(popupSize,targetSize,offset)=>
        {
            // Keep the wider calendar inside this dialog, including at the minimum window width.
            var origin=picker.TranslatePoint(new Point(),this);
            var preferred=picker==ToDate?targetSize.Width-popupSize.Width:0;
            var x=Math.Clamp(preferred,12-origin.X,Math.Max(12-origin.X,ActualWidth-12-origin.X-popupSize.Width));
            return [new(new Point(x,targetSize.Height),PopupPrimaryAxis.Horizontal),
                new(new Point(x,-popupSize.Height),PopupPrimaryAxis.Horizontal)];
        };
        popup.Placement=PlacementMode.Custom;
    }
}
