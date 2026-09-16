using System.Windows;
namespace CloudAlarmOverlay.App.Services;
internal static class SmallNotificationStack
{
    private static readonly List<Window> windows=[];
    public static void Add(Window window){windows.Add(window);Reflow();}
    public static void Remove(Window window){windows.Remove(window);Reflow();}
    private static void Reflow()
    {
        var work=SystemParameters.WorkArea;
        double bottom=work.Bottom-16,right=work.Right-16,columnWidth=0;
        foreach(var w in windows)
        {
            if(bottom-w.Height<work.Top&&bottom<work.Bottom-16)
            {right-=columnWidth+12;bottom=work.Bottom-16;columnWidth=0;}
            w.Left=Math.Max(work.Left,right-w.Width);
            w.Top=Math.Max(work.Top,bottom-w.Height);
            bottom=w.Top-12;columnWidth=Math.Max(columnWidth,w.Width);
        }
    }
}