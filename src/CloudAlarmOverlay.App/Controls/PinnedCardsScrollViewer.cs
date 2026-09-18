using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CloudAlarmOverlay.App.Controls;

// Request room for two cards during measurement. The surrounding dashboard may
// stretch the viewport to match its left column, but more pins never enlarge it.
public sealed class PinnedCardsScrollViewer : ScrollViewer
{
    public const double MinimumCardHeight = 146;
    protected override Size MeasureOverride(Size constraint)
        => base.MeasureOverride(new Size(constraint.Width, Math.Min(constraint.Height, MinimumCardHeight * 2)));
}

public sealed class PinnedCardHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var viewport = values[0] is double height && double.IsFinite(height) ? height : 0;
        var count = values[1] is int total ? total : 0;
        return Math.Max(PinnedCardsScrollViewer.MinimumCardHeight, viewport / Math.Clamp(count, 1, 2));
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
