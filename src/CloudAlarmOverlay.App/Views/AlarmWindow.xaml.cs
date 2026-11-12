using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.App.Views;
public partial class AlarmWindow:Window
{
    private bool allowClose;
    private bool healthDocked;
    private readonly DispatcherTimer timer=new();
    private bool red;
    private readonly TaskCompletionSource<bool> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> Completion=>completion.Task;
    public int? SnoozeMinutes {get;private set;}
    public void DockHealth(string side)
    {
        if (side is not ("Left" or "Center" or "Right")) throw new ArgumentException("健康提醒位置無效。",nameof(side));
        if (!healthDocked) CloudAlarmOverlay.App.Services.SmallNotificationStack.Remove(this);
        healthDocked=true; timer.Stop(); ShowActivated=false; Topmost=true;
        var work=SystemParameters.WorkArea;
        Width=work.Width*.30; Height=work.Height; Top=work.Top;
        Left=side switch { "Left"=>work.Left, "Center"=>work.Left+(work.Width-Width)/2, _=>work.Right-Width };
        var available=Math.Max(0,Width-100);
        ConfirmButton.MinWidth=Math.Min(200,available); ConfirmButton.MaxWidth=Math.Min(340,available);
        ConfirmButton.FontSize=Math.Clamp(Width*.035,18,24);
        NoticeTitle.FontSize=Math.Clamp(Width*.055,28,42);
        DescriptionText.FontSize=Math.Clamp(Width*.038,20,28);
    }
    public AlarmWindow(AlarmViewModel vm,string level,int stackIndex=0,int flashMilliseconds=500,string colorMode="亮色",string colorScheme="依提醒等級")
    {
        InitializeComponent();DataContext=vm;
        void FitContent()
        {
            var compact=ContentSlot.ActualWidth<700;
            Panel.Margin=compact?new Thickness(20,20,20,16):new Thickness(40,34,40,24);
            TitleIcon.Width=TitleIcon.Height=compact?44:64;
            TitleIcon.Margin=compact?new Thickness(0,2,14,0):new Thickness(0,2,20,0);
            TitleIconArtwork.Width=TitleIconArtwork.Height=compact?26:32;
            NoticeTitle.FontSize=healthDocked?Math.Clamp(Width*.055,28,42):compact?22:28;
            ReadingHint.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            Grid.SetRow(ConfirmActions,compact?1:0);
            Grid.SetColumn(ConfirmActions,compact?0:1);
            Grid.SetColumnSpan(ConfirmActions,compact?2:1);
            Grid.SetColumnSpan(SnoozeActions,compact?2:1);
            ConfirmActions.Margin=new Thickness(0,compact&&vm.CanSnooze?14:0,0,0);
        }
        ContentSlot.SizeChanged+=(_,_)=>FitContent();
        var palette=CloudAlarmOverlay.App.Styles.NotificationPalette.Create(level,colorMode,colorScheme,vm.IsPomodoro);
        foreach(var pair in new[]{("NoticeBackground",palette.Background),("NoticeSurface",palette.Surface),("NoticeText",palette.Text),("NoticeMuted",palette.Muted),("NoticeAccent",palette.Accent),("NoticeButtonText",palette.ButtonText)})
            Resources[pair.Item1]=CloudAlarmOverlay.App.Styles.NotificationPalette.Brush(pair.Item2);
        Resources["NoticeLine"]=CloudAlarmOverlay.App.Styles.NotificationPalette.Brush(colorMode=="暗色"?"#526171":"#DCE9E6");
        // Transparency around the card keeps its rounded corners and shadow visible.
        Background=Brushes.Transparent;
        var work=SystemParameters.WorkArea;
        if(level==AlarmLevels.Max)
        {
            Frame.BorderThickness=new Thickness(8);Frame.BorderBrush=CloudAlarmOverlay.App.Styles.NotificationPalette.Brush(palette.Accent);Frame.Margin=new Thickness(0);Frame.CornerRadius=new CornerRadius(0);Topmost=true;Left=0;Top=0;WindowState=WindowState.Maximized;
            timer.Interval=TimeSpan.FromMilliseconds(Math.Clamp(flashMilliseconds,200,5000));
            timer.Tick+=(_,_)=>{red=!red;Frame.BorderBrush=CloudAlarmOverlay.App.Styles.NotificationPalette.Brush(red?(colorMode=="暗色"?"#F66E68":"#E5483F"):palette.Accent);};
        }
        else if(level==AlarmLevels.High)
        {
            Topmost=true;Width=SystemParameters.PrimaryScreenWidth*.775;Height=SystemParameters.PrimaryScreenHeight*.775;
            Left=(SystemParameters.PrimaryScreenWidth-Width)/2;Top=(SystemParameters.PrimaryScreenHeight-Height)/2;
        }
        else
        {
            Topmost=true;Width=Math.Min(480,work.Width-32);Height=Math.Min(vm.HasNote||vm.CanSnooze||vm.CanComplete?620:520,work.Height-32);
            Left=work.Right-Width-16;Top=Math.Max(work.Top,work.Bottom-Height-16-stackIndex*(Height+12));
            if(level==AlarmLevels.Low)
            {
                ShowActivated=false;
            }
        }
        vm.Confirmed+=()=>Finish(true);
        vm.Snoozed+=minutes=>{SnoozeMinutes=minutes;Finish(false);};
        vm.Incorrect+=()=>{
            var movement=new TranslateTransform();CodeInput.RenderTransform=movement;
            movement.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-8,8,TimeSpan.FromMilliseconds(65)){AutoReverse=true,RepeatBehavior=new RepeatBehavior(3)});
        };
        Loaded+=(_,_)=>{if(!healthDocked && level is AlarmLevels.Low or AlarmLevels.Mid)CloudAlarmOverlay.App.Services.SmallNotificationStack.Add(this);if(!healthDocked && level is AlarmLevels.Max)timer.Start();if(vm.RequiresCode)CodeInput.Focus();};
        Closing+=OnClosing;
        Closed+=(_,_)=>{CloudAlarmOverlay.App.Services.SmallNotificationStack.Remove(this);timer.Stop();completion.TrySetResult(false);};
        PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Escape || (e.Key==Key.System && e.SystemKey==Key.F4))e.Handled=true;};
    }
    private void OnHeaderMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton==MouseButton.Left && WindowState!=WindowState.Maximized && !healthDocked)DragMove();
    }
    private void OnClosing(object? sender,CancelEventArgs e){if(!allowClose)e.Cancel=true;}
    public void Finish(bool acknowledged){allowClose=true;completion.TrySetResult(acknowledged);Close();}
}
