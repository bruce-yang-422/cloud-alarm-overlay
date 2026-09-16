using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CloudAlarmOverlay.App.Controls;

/// <summary>A cyclic, five-row time wheel. Pointer gestures remain within the picker.</summary>
public partial class TimeWheel : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(TimeWheel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(TimeWheel), new PropertyMetadata(59, OnMaximumChanged),
        value => (int)value >= 0 && (int)value <= 59);

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    private Point? pressPoint;
    private double dragRemainder;
    private bool dragged;
    private int wheelRemainder;
    private const double RowHeight = 36;

    public TimeWheel()
    {
        InitializeComponent();
        RefreshNumbers();
        DataObject.AddPastingHandler(SelectedInput, (_, e) =>
        {
            var text=e.DataObject.GetData(typeof(string)) as string;
            if(text is null || text.Length>2 || text.Any(c=>c<'0'||c>'9'))e.CancelCommand();
        });
        PreviewMouseWheel += (_, e) =>
        {
            wheelRemainder += e.Delta;
            var steps = wheelRemainder / 120;
            wheelRemainder %= 120;
            if (steps != 0) Step(-steps);
            e.Handled = true;
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if(e.OriginalSource is DependencyObject source && IsInputOrButton(source))return;
            SelectedInput.Focus(); pressPoint = e.GetPosition(Surface); dragRemainder = 0; dragged = false;
            CaptureMouse(); e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (pressPoint is not { } previous || !IsMouseCaptured) return;
            var position = e.GetPosition(Surface);
            var delta = position.Y - previous.Y;
            if (!dragged && Math.Abs(delta) < SystemParameters.MinimumVerticalDragDistance) return;
            dragged = true; pressPoint = position; Drag(delta); e.Handled = true;
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (pressPoint is null) return;
            if (!dragged) Step(Math.Clamp((int)(e.GetPosition(Surface).Y / RowHeight), 0, 4) - 2);
            else Snap();
            pressPoint = null; ReleaseMouseCapture(); e.Handled = true;
        };
        LostMouseCapture += (_, _) => { pressPoint = null; dragRemainder = 0; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down) { CommitInput(); Step(e.Key == Key.Up ? 1 : -1); SelectedInput.SelectAll(); e.Handled = true; }
            else if(e.Key is Key.Enter or Key.Tab)CommitInput();
        };
        GotKeyboardFocus += (_, _) => FocusRing.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 175, 255));
        LostKeyboardFocus += (_, _) => FocusRing.BorderBrush = Brushes.Transparent;
        ManipulationStarting += (_, e) =>
        {
            e.ManipulationContainer = this; e.Mode = ManipulationModes.TranslateY;
            dragRemainder = 0; Focus(); e.Handled = true;
        };
        ManipulationDelta += (_, e) => { Drag(e.DeltaManipulation.Translation.Y); e.Handled = true; };
        ManipulationCompleted += (_, e) => { Snap(); e.Handled = true; };
    }

    private static object CoerceValue(DependencyObject sender, object value)
        => Math.Clamp((int)value, 0, ((TimeWheel)sender).Maximum);
    private static void OnMaximumChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        sender.CoerceValue(ValueProperty);
        ((TimeWheel)sender).RefreshNumbers();
    }
    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((TimeWheel)sender).RefreshNumbers();
    private int Wrap(int number) => (number % (Maximum + 1) + Maximum + 1) % (Maximum + 1);
    private void RefreshNumbers()
    {
        if (Selected is null) return;
        var labels = new[] { AboveTwo, AboveOne, Selected, BelowOne, BelowTwo };
        for (var index = 0; index < labels.Length; index++)
            labels[index].Text = Wrap(Value + index - 2).ToString("D2", CultureInfo.InvariantCulture);
        AutomationProperties.SetHelpText(this, $"目前 {Value:D2}；上下方向鍵、滾輪或拖曳調整。");
        if(SelectedInput is not null)
        {
            SelectedInput.Text=Value.ToString("D2",CultureInfo.InvariantCulture);
            AutomationProperties.SetName(SelectedInput, AutomationProperties.GetName(this));
        }
    }
    private void Step(int steps)
    {
        if (steps == 0) return;
        CommitInput();
        // Preserve the two-way binding while a gesture changes the selected value.
        SetCurrentValue(ValueProperty, Wrap(Value + steps));
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Math.Sign(steps) * 10, 0,
            TimeSpan.FromMilliseconds(110)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
    private void Drag(double delta)
    {
        dragRemainder -= delta;
        var steps = (int)(dragRemainder / RowHeight);
        if (steps == 0) return;
        dragRemainder -= steps * RowHeight;
        Step(steps);
    }
    private void Snap()
    {
        if (Math.Abs(dragRemainder) >= RowHeight / 2) Step(Math.Sign(dragRemainder));
        dragRemainder = 0;
    }
    private bool IsInputOrButton(DependencyObject element)
    {
        for(var current=element; current is not null && current!=this; current=VisualTreeHelper.GetParent(current))
            if(current is TextBox or Button)return true;
        return false;
    }
    public void CommitInput()
    {
        if(int.TryParse(SelectedInput.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var number))
            SetCurrentValue(ValueProperty, Math.Clamp(number,0,Maximum));
        RefreshNumbers();
    }
    private void IncreaseClick(object sender,RoutedEventArgs e){CommitInput();Step(1);e.Handled=true;}
    private void DecreaseClick(object sender,RoutedEventArgs e){CommitInput();Step(-1);e.Handled=true;}
    private void OnTextInput(object sender,TextCompositionEventArgs e)=>e.Handled=e.Text.Any(c=>c<'0'||c>'9');
    private void OnInputFocused(object sender,KeyboardFocusChangedEventArgs e)=>SelectedInput.SelectAll();
    private void OnInputBlurred(object sender,KeyboardFocusChangedEventArgs e)=>CommitInput();
}
