using System.Windows;
using System.Windows.Media;

namespace CloudAlarmOverlay.App.Controls;

/// <summary>A fixed-size countdown ring: a full circle represents the next 24 hours.</summary>
public sealed class CountdownRing : FrameworkElement
{
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(CountdownRing),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(CountdownRing),
        new FrameworkPropertyMetadata(Brushes.Crimson, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush TrackBrush {get=>(Brush)GetValue(TrackBrushProperty);set=>SetValue(TrackBrushProperty,value);}
    public Brush ProgressBrush {get=>(Brush)GetValue(ProgressBrushProperty);set=>SetValue(ProgressBrushProperty,value);}
    public static readonly DependencyProperty RemainingHoursProperty = DependencyProperty.Register(
        nameof(RemainingHours), typeof(double), typeof(CountdownRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double RemainingHours
    {
        get => (double)GetValue(RemainingHoursProperty);
        set => SetValue(RemainingHoursProperty, value);
    }
    protected override void OnRender(DrawingContext dc)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 6);
        dc.DrawEllipse(null, new Pen(TrackBrush, 8), center, radius, radius);
        var fraction = Math.Clamp(RemainingHours / 24, 0, 1);
        if (fraction <= 0) return;
        var pen = new Pen(ProgressBrush, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 1) { dc.DrawEllipse(null, pen, center, radius, radius); return; }
        var angle = fraction * 2 * Math.PI;
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            context.ArcTo(end, new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, arc);
    }
}
