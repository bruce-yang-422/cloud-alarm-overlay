using System.Windows.Media;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.App.Styles;
public sealed record NotificationPalette(string Background,string Surface,string Text,string Muted,string Accent,string ButtonText)
{
    public static NotificationPalette Create(string level,string mode="亮色",string scheme="依提醒等級",bool pomodoro=false)
    {
        // Legacy scheme values no longer override severity colors.
        var family=pomodoro?"珊瑚":level==AlarmLevels.Low?"海灣藍":level==AlarmLevels.Mid?"森林綠":level==AlarmLevels.High?"酒紅":"警示紅";
        var colors=family switch
        {
            "森林綠"=>("#EDF7F2","#173B34","#14715E","#7DDBC3"),
            "酒紅"=>("#FAEEF0","#3B2832","#963D52","#EEA6B6"),
            
            "警示紅"=>("#FFF0EC","#482824","#BC2925","#FFACA3"),
            "珊瑚"=>("#FFF1E9","#412E2A","#B44D32","#FFB297"),
            _=>("#EDF5FF","#23374E","#245FA4","#A0C8FF")
        };
        return mode=="暗色"
            ?new(colors.Item2,family switch{"森林綠"=>"#254A40","酒紅"=>"#4B3540","警示紅"=>"#593730","珊瑚"=>"#533E35",_=>"#30465F"},"#F5F7FB","#CBD5E1",colors.Item4,"#182C40")
            :new(colors.Item1,"#FFFFFF","#20324A","#52647A",colors.Item3,"#FFFFFF");
    }
    public static SolidColorBrush Brush(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
}