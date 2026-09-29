using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;

namespace CloudAlarmOverlay.App.Views;

public partial class HealthToolsView : UserControl
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public HealthToolsView()
    {
        InitializeComponent();
        timer.Tick += (_, _) => { if (IsVisible && DataContext is HealthToolsViewModel vm) vm.Refresh(); };
        Loaded += async (_, _) => { if (DataContext is HealthToolsViewModel vm) await vm.LoadAsync(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
    }
}
public sealed class HealthColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double width && width >= 680 ? 2 : 1;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class HealthEmptyVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
