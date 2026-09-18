using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace CloudAlarmOverlay.App.Views;

public partial class TimePickerWindow : Window
{
    private bool minuteMode, changing, ready, dragging;
    private int hour = 9, minute;
    public string SelectedTime { get; private set; }
    public TimePickerWindow(string value)
    {
        SelectedTime = value;
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        if (TimeOnly.TryParseExact(value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { hour = time.Hour; minute = time.Minute; }
        ready = true; WriteFields(); DrawDial();
    }
    private void WriteFields()
    {
        changing = true; Hours.Text = hour.ToString("00"); Minutes.Text = minute.ToString("00"); changing = false;
        Error.Text = "";
    }
    private bool ValidFields() => int.TryParse(Hours.Text, out var h) && h is >= 0 and <= 23 && int.TryParse(Minutes.Text, out var m) && m is >= 0 and <= 59;
    private void TextEdited(object sender, TextChangedEventArgs e)
    {
        if (!ready || changing) return;
        if (ValidFields()) { hour = int.Parse(Hours.Text); minute = int.Parse(Minutes.Text); Error.Text = ""; DrawDial(); }
        else Error.Text = "小時請輸入 0～23，分鐘請輸入 0～59。";
    }
    private void SelectHours(object sender, RoutedEventArgs e) { minuteMode = false; DrawDial(); }
    private void SelectMinutes(object sender, RoutedEventArgs e) { minuteMode = true; DrawDial(); }
    private void FocusHours(object sender, KeyboardFocusChangedEventArgs e) { if (ready) SelectHours(sender,e); }
    private void FocusMinutes(object sender, KeyboardFocusChangedEventArgs e) { if (ready) SelectMinutes(sender,e); }
    public static int ValueAt(Point point, bool minutes)
    {
        var x = point.X - 145; var y = point.Y - 145;
        var angle = (Math.Atan2(x, -y) + Math.PI * 2) % (Math.PI * 2);
        if (minutes) return (int)Math.Round(angle * 60 / (Math.PI * 2)) % 60;
        return (int)Math.Round(angle * 12 / (Math.PI * 2)) % 12 + (Math.Sqrt(x*x+y*y) < 95 ? 12 : 0);
    }
    private void Pick(Point point)
    {
        if (minuteMode) minute = ValueAt(point,true); else hour = ValueAt(point,false);
        WriteFields(); DrawDial();
    }
    private void DialDown(object sender, MouseButtonEventArgs e) { dragging = true; Dial.CaptureMouse(); Pick(e.GetPosition(Dial)); e.Handled = true; }
    private void DialMove(object sender, MouseEventArgs e) { if (dragging && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(Dial)); }
    private void DialUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        Pick(e.GetPosition(Dial)); dragging = false; Dial.ReleaseMouseCapture();
        if (!minuteMode) { minuteMode = true; DrawDial(); }
    }
    private void DialLostCapture(object sender, MouseEventArgs e) => dragging = false;
    private void DrawDial()
    {
        if (!ready) return;
        Hint.Text = minuteMode ? "選擇分鐘 · 可點選或拖曳至任意一分鐘" : "選擇小時 · 外圈 0～11，內圈 12～23";
        HourMode.FontWeight = minuteMode ? FontWeights.Normal : FontWeights.Bold;
        MinuteMode.FontWeight = minuteMode ? FontWeights.Bold : FontWeights.Normal;
        HourMode.Background = minuteMode ? Background : DialColors.Background;
        MinuteMode.Background = minuteMode ? DialColors.Background : Background;
        Dial.Children.Clear();
        Dial.Children.Add(new Ellipse { Width=290, Height=290, Fill=DialColors.Background, IsHitTestVisible=false });
        var selected = minuteMode ? minute : hour;
        var radius = !minuteMode && hour >= 12 ? 75 : 118;
        var angle = selected * Math.PI * 2 / (minuteMode ? 60 : 12);
        var px = 145 + Math.Sin(angle)*radius; var py = 145 - Math.Cos(angle)*radius;
        Dial.Children.Add(new Line { X1=145,Y1=145,X2=px,Y2=py,Stroke=DialColors.BorderBrush,StrokeThickness=2,IsHitTestVisible=false });
        var dot = new Ellipse { Width=8,Height=8,Fill=DialColors.BorderBrush,IsHitTestVisible=false }; Canvas.SetLeft(dot,141);Canvas.SetTop(dot,141);Dial.Children.Add(dot);
        var selection=new Ellipse { Width=38,Height=38,Fill=SelectionColors.Background,IsHitTestVisible=false };Canvas.SetLeft(selection,px-19);Canvas.SetTop(selection,py-19);Dial.Children.Add(selection);
        for (var n=0;n<(minuteMode?12:24);n++)
        {
            var value=minuteMode?n*5:n;var r=!minuteMode&&n>=12?75:118;var a=n*Math.PI*2/12;
            var button=new Button { Content=value.ToString(minuteMode?"00":"0"),Tag=value,Width=36,Height=36,Padding=new Thickness(0),Margin=new Thickness(0),FontSize=16,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=value==selected?SelectionColors.BorderBrush:Foreground };
            System.Windows.Automation.AutomationProperties.SetName(button,$"{value} {(minuteMode?"分":"時")}");
            button.Click+=(_,_)=>{if(minuteMode)minute=value;else{hour=value;minuteMode=true;}WriteFields();DrawDial();};
            Canvas.SetLeft(button,145+Math.Sin(a)*r-18);Canvas.SetTop(button,145-Math.Cos(a)*r-18);Dial.Children.Add(button);
        }
        if (minuteMode && minute%5!=0)
        {
            var label=new TextBlock { Text=minute.ToString("00"),Foreground=SelectionColors.BorderBrush,FontSize=14,Width=36,TextAlignment=TextAlignment.Center,IsHitTestVisible=false };
            Canvas.SetLeft(label,px-18);Canvas.SetTop(label,py-10);Dial.Children.Add(label);
        }
    }
    private void Accept(object sender, RoutedEventArgs e)
    {
        if (!ValidFields()) { Error.Text="請輸入有效時間（00:00～23:59）。"; return; }
        SelectedTime=$"{int.Parse(Hours.Text):00}:{int.Parse(Minutes.Text):00}";
        DialogResult=true;
    }
}
