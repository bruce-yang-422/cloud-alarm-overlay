using System.Globalization;
namespace CloudAlarmOverlay.Core.Recurrence;
public static class RecurrenceRule
{
    public static void Validate(string rule)
    {
        if(rule is "None" or "Daily") return;
        var parts=rule.Split(':');
        if(parts[0]=="LunarDate")
        {
            if(parts.Length is not (3 or 4) || !int.TryParse(parts[1],out var month) || month is <1 or >12 ||
                !int.TryParse(parts[2],out var day) || day is <1 or >30 || (parts.Length==4 && parts[3] is not ("Regular" or "Both")))
                throw new FormatException("請選擇有效的農曆月份、日期與閏月規則。");
            return;
        }
        if((parts.Length!=2 && !(parts.Length==3 && parts[0]=="Monthly")) || parts[0] is not ("Weekly" or "Monthly" or "LunarDay")) throw new FormatException("重複規則無效。");
        if(parts.Length==3 && parts[2].Split(',').Any(s=>!int.TryParse(s,out var month)||month<1||month>12)) throw new FormatException("請至少選擇一個有效月份（1–12）。");
        int max=parts[0]=="Weekly"?7:parts[0]=="Monthly"?31:30;
        var values=parts[1].Split(',');
        if(parts[0]=="Monthly" && values.Length!=1) throw new FormatException("每月僅能選一日。");
        if(values.Any(s=>!int.TryParse(s,out int n)||n<1||n>max)) throw new FormatException("重複日期超出範圍。");
    }
    public static bool Matches(string rule,DateOnly date,DateTime scheduled,int? lunarDay=null)
    {
        Validate(rule);
        if(date<DateOnly.FromDateTime(scheduled)) return false;
        if(rule=="None") return date==DateOnly.FromDateTime(scheduled);
        if(rule=="Daily") return true;
        var p=rule.Split(':');
        if(p[0]=="LunarDate")
        {
            var solar=date.ToDateTime(TimeOnly.MinValue);
            var calendar=new ChineseLunisolarCalendar();
            if(solar<calendar.MinSupportedDateTime || solar>calendar.MaxSupportedDateTime) return false;
            var leap=calendar.GetLeapMonth(calendar.GetYear(solar));
            var rawMonth=calendar.GetMonth(solar);
            var isLeap=leap>0 && rawMonth==leap;
            var month=leap>0 && rawMonth>=leap?rawMonth-1:rawMonth;
            return month==int.Parse(p[1]) && calendar.GetDayOfMonth(solar)==int.Parse(p[2]) && (!isLeap || (p.Length==4 && p[3]=="Both"));
        }
        if(p.Length==3 && !p[2].Split(',').Select(int.Parse).Contains(date.Month))return false;
        int value;
        if(p[0]=="LunarDay")
        {
            var calendar=new ChineseLunisolarCalendar();
            var solar=date.ToDateTime(TimeOnly.MinValue);
            if(solar<calendar.MinSupportedDateTime || solar>calendar.MaxSupportedDateTime)return false;
            // Monthly recurrence includes regular and leap months. Legacy Sheet values are ignored.
            value=calendar.GetDayOfMonth(solar);
        }
        else value=p[0]=="Weekly"?(date.DayOfWeek==DayOfWeek.Sunday?7:(int)date.DayOfWeek):date.Day;
        return p[1].Split(',').Select(int.Parse).Contains(value);
    }
    public static string Describe(string rule) => rule switch
    {
        "None" => "不重複", "Daily" => "每天", "Weekly:1,2,3,4,5" => "每個工作日",
        _ when rule.StartsWith("LunarDate:", StringComparison.Ordinal) => $"每年農曆 {rule.Split(':')[1]} 月 {rule.Split(':')[2]} 日（{(rule.EndsWith(":Both",StringComparison.Ordinal)?"含閏月":"不含閏月")}）",
        _ when rule.StartsWith("Monthly:", StringComparison.Ordinal) => MonthlyLabel(rule.Split(':')),
        _ => rule.Replace("Weekly:", "每週 ").Replace("LunarDay:", "農曆 ")
    };
    private static string MonthlyLabel(string[] parts) => parts.Length==3
        ? !parts[2].Contains(',') ? $"每年國曆 {parts[2]} 月 {parts[1]} 日" : $"{parts[2]} 月的 {parts[1]} 日" : $"每月 {parts[1]} 日";
    public static string NormalizeCsv(string? value,DateTime scheduled)
    {
        var v=(value??"").Trim();
        var result=v.ToUpperInvariant() switch
        {
            "" or "ONCE" or "NONE"=>"None", "DAILY"=>"Daily",
            "WORKDAY" or "WEEKLY"=>"Weekly:1,2,3,4,5", "MONTHLY"=>"Monthly:"+scheduled.Day.ToString(CultureInfo.InvariantCulture),
            _ when v.StartsWith("WEEKLY:",StringComparison.OrdinalIgnoreCase)=>"Weekly:"+v[7..],
            _ when v.StartsWith("MONTHLY:",StringComparison.OrdinalIgnoreCase)=>"Monthly:"+v[8..],
            _ when v.StartsWith("LUNARDATE:",StringComparison.OrdinalIgnoreCase)=>"LunarDate:"+v[10..],
            _ when v.StartsWith("LUNARDAY:",StringComparison.OrdinalIgnoreCase)=>"LunarDay:"+v[9..],
            _=>v
        };
        Validate(result);
        return result;
    }
}
