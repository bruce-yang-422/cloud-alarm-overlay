using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CloudAlarmOverlay.App.Controls;

public partial class CyclicTimeSelector : UserControl
{
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(int),typeof(CyclicTimeSelector),
        new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,ValueChanged,CoerceValue));
    public static readonly DependencyProperty MaximumProperty=DependencyProperty.Register(nameof(Maximum),typeof(int),typeof(CyclicTimeSelector),
        new PropertyMetadata(59,(d,_)=>d.CoerceValue(ValueProperty)),value=>(int)value is >=0 and <=59);
    public int Value { get=>(int)GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    public int Maximum { get=>(int)GetValue(MaximumProperty); set=>SetValue(MaximumProperty,value); }
    private int wheelRemainder;
    private bool editing;

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
        IsKeyboardFocusWithinChanged+=(_,_)=>Surface.BorderThickness=new Thickness(IsKeyboardFocusWithin?2:1);
        MouseLeave+=(_,_)=>wheelRemainder=0;
    }
    private static void ValueChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        var selector=(CyclicTimeSelector)d;
        if(!selector.editing && selector.ValueEditor is not null)selector.ValueEditor.Text=((int)e.NewValue).ToString("D2");
    }
    private bool TryReadInput(out int value)
    {
        var text=ValueEditor.Text;
        value=0;
        return text.Length is >0 and <=2 && text.All(c=>c is >= '0' and <= '9')
            && int.TryParse(text,out value) && value<=Maximum;
    }
    public bool TryCommitInput()
    {
        if(!TryReadInput(out var value))return false;
        SetCurrentValue(ValueProperty,value);
        ValueEditor.Text=value.ToString("D2");
        return true;
    }
    private void EditValue(object sender,TextChangedEventArgs e)
    {
        if(ValueEditor is null || !TryReadInput(out var value))return;
        editing=true;
        try{SetCurrentValue(ValueProperty,value);}
        finally{editing=false;}
    }
    private void SelectValue(object sender,KeyboardFocusChangedEventArgs e)=>ValueEditor.SelectAll();
    private void FocusEditor(object sender,MouseButtonEventArgs e)
    {
        if(ValueEditor.IsKeyboardFocusWithin)return;
        ValueEditor.Focus();e.Handled=true;
    }
    private void FinishEditing(object sender,KeyboardFocusChangedEventArgs e)=>TryCommitInput();
    private static object CoerceValue(DependencyObject d,object value)=>Math.Clamp((int)value,0,((CyclicTimeSelector)d).Maximum);
    private void Step(int steps)
    {
        if(steps==0)return;
        var count=Maximum+1;
        SetCurrentValue(ValueProperty,((Value+steps)%count+count)%count);
        ValueEditor.Text=Value.ToString("D2");
    }
    private void Previous(object sender,RoutedEventArgs e){Step(-1);e.Handled=true;}
    private void Next(object sender,RoutedEventArgs e){Step(1);e.Handled=true;}
}
