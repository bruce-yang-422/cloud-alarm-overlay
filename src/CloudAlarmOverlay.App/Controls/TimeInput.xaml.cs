using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.Views;
namespace CloudAlarmOverlay.App.Controls;
public partial class TimeInput : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TimeInput), new FrameworkPropertyMetadata("09:00", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public TimeInput() => InitializeComponent();
    private void OpenClock(object sender, RoutedEventArgs e)
    {
        var picker = new TimePickerWindow(Text) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true) SetCurrentValue(TextProperty, picker.SelectedTime);
    }
}
