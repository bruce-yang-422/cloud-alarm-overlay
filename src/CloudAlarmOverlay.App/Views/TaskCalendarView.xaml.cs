using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;
public sealed class CalendarHeightConverter:System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values,Type targetType,object parameter,System.Globalization.CultureInfo culture)
        =>Math.Max(values[0] is double h?h:0,(values[1] is int count?count:5)*136);
    public object[] ConvertBack(object value,Type[] targetTypes,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class CalendarDateSelectedConverter:System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values,Type targetType,object parameter,System.Globalization.CultureInfo culture)
        =>values.Length==2 && values[0] is DateTime date && values[1] is DateTime selected && date.Date==selected.Date;
    public object[] ConvertBack(object value,Type[] targetTypes,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
}
public partial class TaskCalendarView:UserControl
{
    private TaskCalendarViewModel? calendar;
    public TaskCalendarView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>AttachCalendar();
        Unloaded+=(_,_)=>DetachCalendar();
        DataContextChanged+=(_,_)=>{if(IsLoaded)AttachCalendar();};
    }
    private void AttachCalendar()
    {
        DetachCalendar();calendar=(DataContext as MainViewModel)?.TaskCalendar;
        if(calendar is not null)calendar.PropertyChanged+=CalendarChanged;
        RevealSelectedDay();
    }
    private void DetachCalendar(){if(calendar is not null)calendar.PropertyChanged-=CalendarChanged;calendar=null;}
    private void CalendarChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(TaskCalendarViewModel.SelectedDay) or nameof(TaskCalendarViewModel.Weeks))RevealSelectedDay();
    }
    private void ShowSelectedDay(object sender,RoutedEventArgs e)=>RevealSelectedDay();
    private void RevealSelectedDay()=>Dispatcher.InvokeAsync(()=>
    {
        if(!IsLoaded || calendar is null)return;
        UpdateLayout();
        var button=Descendants(MonthScroll).OfType<Button>().FirstOrDefault(b=>b.DataContext is CalendarDay d && d.Date==calendar.SelectedDay.Date);
        if(button is null)return;
        var top=button.TranslatePoint(new Point(),MonthScroll).Y;
        if(top<0)MonthScroll.ScrollToVerticalOffset(MonthScroll.VerticalOffset+top);
        else if(top+button.ActualHeight>MonthScroll.ViewportHeight)
            MonthScroll.ScrollToVerticalOffset(MonthScroll.VerticalOffset+top+button.ActualHeight-MonthScroll.ViewportHeight);
    },DispatcherPriority.Loaded);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
    }
    private void Preview(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.DataContext is CalendarEntry entry)
            new TaskPreviewWindow(new(entry.Task)){Owner=Window.GetWindow(this)}.ShowDialog();
    }
}
