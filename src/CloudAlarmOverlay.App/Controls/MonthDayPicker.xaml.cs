using System.Windows;
using System.Windows.Controls;
namespace CloudAlarmOverlay.App.Controls;
public partial class MonthDayPicker : UserControl
{
    public static readonly DependencyProperty MonthProperty=DependencyProperty.Register(nameof(Month),typeof(int),typeof(MonthDayPicker),new FrameworkPropertyMetadata(1,FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,Changed));
    public static readonly DependencyProperty DayProperty=DependencyProperty.Register(nameof(Day),typeof(int),typeof(MonthDayPicker),new FrameworkPropertyMetadata(1,FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,Changed));
    public int Month {get=>(int)GetValue(MonthProperty);set=>SetValue(MonthProperty,value);}
    public int Day {get=>(int)GetValue(DayProperty);set=>SetValue(DayProperty,value);}
    private int displayMonth = 1;
    public MonthDayPicker(){InitializeComponent();DatePopup.Opened+=(_,_)=>Refresh();Refresh();}
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)=>((MonthDayPicker)d).Refresh();
    private void Refresh()
    {
        if(SelectionLabel is null)return;
        SelectionLabel.Text=$"每年 {Month:00} 月 {Day:00} 日";
        displayMonth=Month;DrawMonth();
    }
    private void DrawMonth()
    {
        MonthHeading.Content=$"{displayMonth} 月 ▾";
        DayGrid.Children.Clear();MonthGrid.Children.Clear();
        if(displayMonth is <1 or >12)return;
        for(var i=1;i<=12;i++)
        {
            var value=i;var button=new Button{Content=$"{i} 月",Margin=new Thickness(2),Padding=new Thickness(4,8,4,8)};
            button.Click+=(_,_)=>{ChooseMonth(value);MonthGrid.Visibility=Visibility.Collapsed;DayGrid.Visibility=Visibility.Visible;};MonthGrid.Children.Add(button);
        }
        for(var i=1;i<=DateTime.DaysInMonth(2000,displayMonth);i++)
        {
            var value=i;var selectedMonth=displayMonth;var button=new Button{Content=i,Margin=new Thickness(2),Padding=new Thickness(2,8,2,8),Style=(Style)FindResource(i==Day && displayMonth==Month?"Primary":typeof(Button))};
            System.Windows.Automation.AutomationProperties.SetName(button,$"{displayMonth} 月 {i} 日");
            button.Click+=(_,_)=>{SetCurrentValue(DayProperty,value);SetCurrentValue(MonthProperty,selectedMonth);OpenButton.IsChecked=false;OpenButton.Focus();};DayGrid.Children.Add(button);
        }
    }
    private void ChooseMonth(int value)
    {
        displayMonth=value;DrawMonth();
    }
    private void PreviousMonth(object sender,RoutedEventArgs e)=>ChooseMonth(displayMonth<=1?12:displayMonth-1);
    private void NextMonth(object sender,RoutedEventArgs e)=>ChooseMonth(displayMonth>=12?1:displayMonth+1);
    private void PopupKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key==System.Windows.Input.Key.Escape){OpenButton.IsChecked=false;OpenButton.Focus();e.Handled=true;}
    }

    private void ShowMonths(object sender,RoutedEventArgs e)
    {
        var months=MonthGrid.Visibility!=Visibility.Visible;
        MonthGrid.Visibility=months?Visibility.Visible:Visibility.Collapsed;DayGrid.Visibility=months?Visibility.Collapsed:Visibility.Visible;
    }
}
