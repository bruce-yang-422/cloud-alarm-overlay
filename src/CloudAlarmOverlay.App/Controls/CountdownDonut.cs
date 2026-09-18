using System.Windows;
using System.Windows.Media;

namespace CloudAlarmOverlay.App.Controls;

/// <summary>A native vector donut; values and brushes update without bitmap assets.</summary>
public sealed class CountdownDonut : FrameworkElement
{
    public static readonly DependencyProperty FirstProperty = Number(nameof(First));
    public static readonly DependencyProperty SecondProperty = Number(nameof(Second));
    public static readonly DependencyProperty ThirdProperty = Number(nameof(Third));
    public static readonly DependencyProperty FourthProperty = Number(nameof(Fourth));
    public static readonly DependencyProperty FifthProperty = Number(nameof(Fifth));
    public static readonly DependencyProperty FourthBrushProperty = Color(nameof(FourthBrush), Brushes.MediumPurple);
    public static readonly DependencyProperty FifthBrushProperty = Color(nameof(FifthBrush), Brushes.DarkOrange);
    public double Fourth { get => (double)GetValue(FourthProperty); set => SetValue(FourthProperty, value); }
    public double Fifth { get => (double)GetValue(FifthProperty); set => SetValue(FifthProperty, value); }
    public Brush FourthBrush { get => (Brush)GetValue(FourthBrushProperty); set => SetValue(FourthBrushProperty, value); }
    public Brush FifthBrush { get => (Brush)GetValue(FifthBrushProperty); set => SetValue(FifthBrushProperty, value); }
    public static readonly DependencyProperty FirstBrushProperty = Color(nameof(FirstBrush), Brushes.DodgerBlue);
    public static readonly DependencyProperty SecondBrushProperty = Color(nameof(SecondBrush), Brushes.MediumSeaGreen);
    public static readonly DependencyProperty ThirdBrushProperty = Color(nameof(ThirdBrush), Brushes.IndianRed);
    public static readonly DependencyProperty TrackBrushProperty = Color(nameof(TrackBrush), Brushes.LightGray);
    private static DependencyProperty Number(string name) => DependencyProperty.Register(name, typeof(double), typeof(CountdownDonut), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private static DependencyProperty Color(string name, Brush fallback) => DependencyProperty.Register(name, typeof(Brush), typeof(CountdownDonut), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));
    public double First { get => (double)GetValue(FirstProperty); set => SetValue(FirstProperty, value); }
    public double Second { get => (double)GetValue(SecondProperty); set => SetValue(SecondProperty, value); }
    public double Third { get => (double)GetValue(ThirdProperty); set => SetValue(ThirdProperty, value); }
    public Brush FirstBrush { get => (Brush)GetValue(FirstBrushProperty); set => SetValue(FirstBrushProperty, value); }
    public Brush SecondBrush { get => (Brush)GetValue(SecondBrushProperty); set => SetValue(SecondBrushProperty, value); }
    public Brush ThirdBrush { get => (Brush)GetValue(ThirdBrushProperty); set => SetValue(ThirdBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var thickness = size * .14;
        var radius = (size - thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(TrackBrush, thickness), center, radius, radius);
        var values = new[] { First, Second, Third, Fourth, Fifth }.Select(v => double.IsFinite(v) ? Math.Max(0, v) : 0).ToArray();
        var total = values.Sum();
        if (total <= 0) return;
        var brushes = new[] { FirstBrush, SecondBrush, ThirdBrush, FourthBrush, FifthBrush };
        double angle = -90;
        for (var i = 0; i < values.Length; i++)
        {
            var sweep = values[i] / total * 360;
            if (sweep <= 0) continue;
            var pen = new Pen(brushes[i], thickness);
            if (sweep >= 359.999) dc.DrawEllipse(null, pen, center, radius, radius);
            else
            {
                Point At(double degrees) => new(center.X + radius * Math.Cos(degrees * Math.PI / 180), center.Y + radius * Math.Sin(degrees * Math.PI / 180));
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(At(angle), false, false);
                    context.ArcTo(At(angle + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
                }
                geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
            }
            angle += sweep;
        }
    }
}
