using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CloudAlarmOverlay.App.Controls;

public partial class CyclicTimeSelector : UserControl
{
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(int),typeof(CyclicTimeSelector),
        new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,null,CoerceValue));
    public static readonly DependencyProperty MaximumProperty=DependencyProperty.Register(nameof(Maximum),typeof(int),typeof(CyclicTimeSelector),
        new PropertyMetadata(59,(d,_)=>d.CoerceValue(ValueProperty)),value=>(int)value is >=0 and <=59);
    public int Value { get=>(int)GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    public int Maximum { get=>(int)GetValue(MaximumProperty); set=>SetValue(MaximumProperty,value); }
    private int wheelRemainder;

    public CyclicTimeSelector()
    {
        InitializeComponent();
        PreviewMouseWheel+=(_,e)=>
        {
            wheelRemainder+=e.Delta;
            var steps=wheelRemainder/120;
            wheelRemainder%=120;
            Step(-steps);
            e.Handled=true;
        };
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key is not (Key.Up or Key.Down))return;
            Step(e.Key==Key.Down?1:-1);e.Handled=true;
        };
        MouseLeftButtonDown+=(_,_)=>Focus();
        IsKeyboardFocusWithinChanged+=(_,_)=>Surface.BorderThickness=new Thickness(IsKeyboardFocusWithin?2:1);
        MouseLeave+=(_,_)=>wheelRemainder=0;
    }
    private static object CoerceValue(DependencyObject d,object value)=>Math.Clamp((int)value,0,((CyclicTimeSelector)d).Maximum);
    private void Step(int steps)
    {
        if(steps==0)return;
        var count=Maximum+1;
        SetCurrentValue(ValueProperty,((Value+steps)%count+count)%count);
    }
    private void Previous(object sender,RoutedEventArgs e){Step(-1);e.Handled=true;}
    private void Next(object sender,RoutedEventArgs e){Step(1);e.Handled=true;}
}
