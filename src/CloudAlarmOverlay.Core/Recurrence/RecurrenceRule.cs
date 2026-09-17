using System.Globalization;
namespace CloudAlarmOverlay.Core.Recurrence;
public static class RecurrenceRule
{
    public static void Validate(string rule)
    {
        if(rule is "None" or "Daily") return;
        var parts=rule.Split(':');
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
        if(p.Length==3 && !p[2].Split(',').Select(int.Parse).Contains(date.Month))return false;
        int value=p[0] switch {"Weekly"=>date.DayOfWeek==DayOfWeek.Sunday?7:(int)date.DayOfWeek,"Monthly"=>date.Day,_=>lunarDay??0};
        return p[1].Split(',').Select(int.Parse).Contains(value);
    }
    public static string Describe(string rule) => rule switch
    {
        "None" => "不重複", "Daily" => "每天", "Weekly:1,2,3,4,5" => "每個工作日",
        _ when rule.StartsWith("Monthly:", StringComparison.Ordinal) => MonthlyLabel(rule.Split(':')),
        _ => rule.Replace("Weekly:", "每週 ").Replace("LunarDay:", "農曆 ")
    };
    private static string MonthlyLabel(string[] parts) => parts.Length==3
        ? $"{parts[2]} 月的 {parts[1]} 日" : $"每月 {parts[1]} 日";
    public static string NormalizeCsv(string? value,DateTime scheduled)
    {
        var v=(value??"").Trim();
        var result=v.ToUpperInvariant() switch
        {
            "" or "ONCE" or "NONE"=>"None", "DAILY"=>"Daily",
            "WORKDAY" or "WEEKLY"=>"Weekly:1,2,3,4,5", "MONTHLY"=>"Monthly:"+scheduled.Day.ToString(CultureInfo.InvariantCulture),
            _ when v.StartsWith("WEEKLY:",StringComparison.OrdinalIgnoreCase)=>"Weekly:"+v[7..],
            _ when v.StartsWith("MONTHLY:",StringComparison.OrdinalIgnoreCase)=>"Monthly:"+v[8..],
            _ when v.StartsWith("LUNARDAY:",StringComparison.OrdinalIgnoreCase)=>"LunarDay:"+v[9..],
            _=>v
        };
        Validate(result);
        return result;
    }
}
