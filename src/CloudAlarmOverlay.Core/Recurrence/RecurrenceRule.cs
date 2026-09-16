using System.Globalization;
namespace CloudAlarmOverlay.Core.Recurrence;
public static class RecurrenceRule
{
    public static void Validate(string rule)
    {
        if(rule is "None" or "Daily") return;
        var parts=rule.Split(':');
        if(parts.Length!=2 || parts[0] is not ("Weekly" or "Monthly" or "LunarDay")) throw new FormatException("重複規則無效。");
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
        int value=p[0] switch {"Weekly"=>date.DayOfWeek==DayOfWeek.Sunday?7:(int)date.DayOfWeek,"Monthly"=>date.Day,_=>lunarDay??0};
        return p[1].Split(',').Select(int.Parse).Contains(value);
    }
    public static string NormalizeCsv(string? value,DateTime scheduled)
    {
        var v=(value??"").Trim();
        var result=v.ToUpperInvariant() switch
        {
            "" or "ONCE" or "NONE"=>"None", "DAILY"=>"Daily",
            "WEEKLY"=>"Weekly:1,2,3,4,5", "MONTHLY"=>"Monthly:"+scheduled.Day.ToString(CultureInfo.InvariantCulture),
            _=>v
        };
        Validate(result);
        return result;
    }
}
