using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CloudAlarmOverlay.App.Controls;

public sealed class SlidingSegmentedControl : ListBox
{
    private Canvas? rail;
    private Border? indicator;
    public SlidingSegmentedControl() => SizeChanged += (_, _) => PositionIndicator(false);
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        rail = GetTemplateChild("PART_Rail") as Canvas;
        indicator = GetTemplateChild("PART_Indicator") as Border;
        Dispatcher.BeginInvoke(() => PositionIndicator(false), DispatcherPriority.Loaded);
    }
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        Dispatcher.BeginInvoke(() => PositionIndicator(true), DispatcherPriority.Loaded);
    }
    private void PositionIndicator(bool animate)
    {
        if (rail is null || indicator is null) return;
        if (ItemContainerGenerator.ContainerFromIndex(SelectedIndex) is not ListBoxItem item || item.ActualWidth <= 0)
        { indicator.Visibility = Visibility.Hidden; return; }
        indicator.Visibility = Visibility.Visible;
        var point = item.TransformToAncestor(rail.TemplatedParent as FrameworkElement ?? this).Transform(new Point());
        var origin = rail.TransformToAncestor(this).Transform(new Point());
        indicator.Height = item.ActualHeight;
        var duration = TimeSpan.FromMilliseconds(animate && SystemParameters.ClientAreaAnimation ? 160 : 0);
        indicator.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(point.X - origin.X, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        indicator.BeginAnimation(WidthProperty, new DoubleAnimation(item.ActualWidth, duration));
    }
}
