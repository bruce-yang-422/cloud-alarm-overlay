using System.ComponentModel;
using System.Windows;
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
    private readonly DispatcherTimer timer=new();
    private int remaining=10;
    private bool red;
    private readonly TaskCompletionSource<bool> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> Completion=>completion.Task;
    public AlarmWindow(AlarmViewModel vm,string level,int stackIndex=0,int flashMilliseconds=500,string colorMode="亮色",string colorScheme="依提醒等級")
    {
        InitializeComponent();DataContext=vm;
        void FitContent()
        {
            NoticeBody.MaxHeight=Math.Max(0,ContentSlot.ActualHeight);
            NoticeScroll.MaxHeight=Math.Max(0,ContentSlot.ActualHeight-Actions.ActualHeight-24);
        }
        ContentSlot.SizeChanged+=(_,_)=>FitContent();
        Actions.SizeChanged+=(_,_)=>FitContent();
        var palette=CloudAlarmOverlay.App.Styles.NotificationPalette.Create(level,colorMode,colorScheme,vm.IsPomodoro);
        foreach(var pair in new[]{("NoticeBackground",palette.Background),("NoticeSurface",palette.Surface),("NoticeText",palette.Text),("NoticeMuted",palette.Muted),("NoticeAccent",palette.Accent),("NoticeButtonText",palette.ButtonText)})
            Resources[pair.Item1]=CloudAlarmOverlay.App.Styles.NotificationPalette.Brush(pair.Item2);
        var work=SystemParameters.WorkArea;
        if(level==AlarmLevels.Max)
        {
            Frame.BorderThickness=new Thickness(8);Topmost=true;Left=0;Top=0;WindowState=WindowState.Maximized;
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
            Topmost=level==AlarmLevels.Mid;Width=Math.Min(430,work.Width-32);Height=Math.Min(vm.HasNote?520:430,work.Height-32);
            Left=work.Right-Width-16;Top=Math.Max(work.Top,work.Bottom-Height-16-stackIndex*(Height+12));
            if(level==AlarmLevels.Low)
            {
                ShowActivated=false;timer.Interval=TimeSpan.FromSeconds(1);
                timer.Tick+=(_,_)=>{if(!IsMouseOver&&--remaining<=0)Finish(true);};
            }
        }
        vm.Confirmed+=()=>Finish(true);
        vm.Incorrect+=()=>{
            var movement=new TranslateTransform();CodeInput.RenderTransform=movement;
            movement.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-8,8,TimeSpan.FromMilliseconds(65)){AutoReverse=true,RepeatBehavior=new RepeatBehavior(3)});
        };
        Loaded+=(_,_)=>{if(level is AlarmLevels.Low or AlarmLevels.Mid)CloudAlarmOverlay.App.Services.SmallNotificationStack.Add(this);if(level is AlarmLevels.Max or AlarmLevels.Low)timer.Start();if(vm.RequiresCode)CodeInput.Focus();};
        Closing+=OnClosing;
        Closed+=(_,_)=>{CloudAlarmOverlay.App.Services.SmallNotificationStack.Remove(this);timer.Stop();completion.TrySetResult(false);};
        PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Escape || (e.Key==Key.System && e.SystemKey==Key.F4))e.Handled=true;};
    }
    private void OnClosing(object? sender,CancelEventArgs e){if(!allowClose)e.Cancel=true;}
    public void Finish(bool acknowledged){allowClose=true;completion.TrySetResult(acknowledged);Close();}
}
