using System.Globalization;
namespace CloudAlarmOverlay.Core.Models;

// Contains only public card content; notes and reminder configuration never enter the renderer.
public sealed record CountdownShareSnapshot(string Title, string Category, string Direction, string Lead,
    string Value, string Unit, string DateCaption, string Status, DateTime CapturedAt)
{
    public const string BrandingSettingKey = "CountdownShareBranding";
    public static CountdownShareSnapshot Create(CountdownItem item, DateTime now,
        IReadOnlyDictionary<DateOnly,int>? lunar = null, IReadOnlyList<Holiday>? holidays = null)
    {
        var at = item.Mode == "Days" ? now.Date : now;
        var target = item.DisplayTarget(now,lunar,holidays);
        var date = (item.IsCountUp ? "起始日期" : "目標日期") + "  " + target.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture);
        var lead = item.IsCountUp ? "已過" : "還有";
        string value; string unit = ""; string status = item.IsCompleted ? "已完成" : "";
        if (item.IsCountUp)
        {
            var end = item.CompletedAt ?? now;
            if(item.Mode == "Days") end=end.Date;
            if(end < item.TargetAt) { lead="";value="尚未開始"; }
            else (value,unit)=Interval(item.TargetAt,end,item.DisplayFormat);
        }
        else if(item.IsCompleted) { lead="";value="已完成"; }
        else if(item.EffectiveRecurrence!="None" && item.NextTarget(at,lunar,holidays) is null)
        { lead="";value="待確認日期";date="尚無可用目標日期"; }
        else if(target<at || item.Mode=="Time" && target==at) { lead="";value="已到期"; }
        else if(target==at) { lead="";value="就是今天"; }
        else if(item.Mode=="Time" && target-at<TimeSpan.FromDays(1)) { value="不到 1";unit="天"; }
        else (value,unit)=Interval(at,target,item.DisplayFormat);
        return new(item.Title,item.Category,item.IsCountUp?"正數紀念":"倒數期待",lead,value,unit,date,status,now);
    }
    private static (string Value,string Unit) Interval(DateTime start,DateTime end,string format) =>
        format=="Days" ? (((long)(end-start).TotalDays).ToString("N0",CultureInfo.InvariantCulture),"天") :
        (CountdownItem.FormatInterval(start,end,format),"");
}
