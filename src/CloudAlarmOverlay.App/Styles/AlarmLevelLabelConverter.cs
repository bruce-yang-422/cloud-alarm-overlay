using System.Globalization;
using System.Windows.Data;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.App.Styles;
public sealed class AlarmLevelLabelConverter:IValueConverter
{
    public static string Label(string level)=>level switch
    {
        AlarmLevels.Low=>"一般提醒",
        AlarmLevels.Mid=>"重要提醒",
        AlarmLevels.High=>"緊急提醒",
        AlarmLevels.Max=>"強制通知",
        _=>level
    };
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>Label(value as string??"");
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
