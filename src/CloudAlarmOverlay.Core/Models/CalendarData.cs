using System.Text.Json;

namespace CloudAlarmOverlay.Core.Models;

public sealed record CalendarFile<T>(int SchemaVersion, string Kind, string Version, DateOnly From, DateOnly To, T[] Entries);
public sealed record CalendarUpdateOptions
{
    private const string Root="https://raw.githubusercontent.com/bruce-yang-422/cloud-alarm-overlay/main/data/calendar/";
    public bool Enabled { get; init; }
    public string Frequency { get; init; }="Daily";
    public string HolidaysUrl { get; init; }=Root+"SheetA_Holidays.json";
    public string LunarUrl { get; init; }=Root+"SheetA_LunarCalendar.json";
    public void Validate()
    {
        if(Frequency is not ("Daily" or "Weekly" or "Monthly" or "Manual"))throw new ArgumentException("請選擇每天、每週、每月或手動更新。");
        NormalizeUrl(HolidaysUrl);NormalizeUrl(LunarUrl);
    }
    public static string NormalizeUrl(string url)
    {
        if(!Uri.TryCreate(url.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!="")
            throw new ArgumentException("請使用公開 GitHub JSON 的 HTTPS 網址，不含登入資訊、查詢參數或錨點。");
        var parts=uri.AbsolutePath.Trim('/').Split('/');
        if(uri.Host=="github.com" && parts.Length>=5 && parts[2] is "blob" or "raw")
            uri=new Uri("https://raw.githubusercontent.com/"+string.Join('/',parts.Take(2).Concat(parts.Skip(3))));
        if(uri.Host!="raw.githubusercontent.com"||uri.AbsolutePath.Trim('/').Split('/').Length<4||!uri.AbsolutePath.EndsWith(".json",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("請填 GitHub 的 JSON 檔案網址（raw.githubusercontent.com 或 github.com/.../blob/...）。");
        return uri.AbsoluteUri;
    }
    public bool IsDue(DateTimeOffset now, DateTimeOffset? lastAttempt)
    {
        if(!Enabled||Frequency=="Manual")return false;
        if(lastAttempt is null)return true;
        var previous=lastAttempt.Value.ToOffset(now.Offset).Date;
        var today=now.Date;
        return Frequency switch
        {
            "Daily"=>previous<today,
            "Weekly"=>previous<today.AddDays(-((int)today.DayOfWeek+6)%7),
            "Monthly"=>previous<new DateTime(today.Year,today.Month,1),
            _=>false
        };
    }
}
public sealed record CalendarDataState
{
    public const string Key="CalendarDataV1";
    public CalendarUpdateOptions Options { get; init; }=new();
    public CalendarFile<Holiday>? Holidays { get; init; }
    public CalendarFile<LunarCalendarEntry>? Lunar { get; init; }
    public string Source { get; init; }="BuiltIn";
    public DateTimeOffset? AppliedAt { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public string LastResult { get; init; }="尚未啟用網路更新";
}
public static class CalendarJson
{
    public static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    public static CalendarFile<Holiday> ReadHolidays(string json)
    {
        var file=Read<Holiday>(json,"Holidays",h=>h.Date);
        if(file.Entries.Any(h=>h.Type is not ("國定假日" or "全公司停班" or "補班日" or "其他") || h.Note?.Length>1000))throw new FormatException("假日類型或備註無效。");
        return file;
    }
    public static CalendarFile<LunarCalendarEntry> ReadLunar(string json)
    {
        var file=Read<LunarCalendarEntry>(json,"LunarCalendar",l=>l.Date);
        if(file.Entries.Any(l=>l.LunarDay is <1 or >30 || l.LunarDate?.Length>100 || l.SolarTerm?.Length>100))throw new FormatException("農曆日期或節氣無效。");
        if(file.Entries.Length!=file.To.DayNumber-file.From.DayNumber+1)throw new FormatException("農曆 JSON 必須包含涵蓋期間的每一天。");
        return file;
    }
    private static CalendarFile<T> Read<T>(string json,string kind,Func<T,DateOnly> date)
    {
        CalendarFile<T> file;
        try{file=JsonSerializer.Deserialize<CalendarFile<T>>(json,Options)??throw new FormatException("JSON 不可為空。");}
        catch(JsonException ex){throw new FormatException("日曆 JSON 格式錯誤。",ex);}
        if(file.SchemaVersion!=1||file.Kind!=kind||string.IsNullOrWhiteSpace(file.Version)||file.Version.Length>80||file.From==default||file.To<file.From||file.To.DayNumber-file.From.DayNumber>7305||file.Entries is null||file.Entries.Length>10000)
            throw new FormatException("日曆 JSON 版本、類型或涵蓋期間無效。");
        if(file.Entries.Any(e=>e is null||date(e)<file.From||date(e)>file.To)||file.Entries.Select(date).Distinct().Count()!=file.Entries.Length)
            throw new FormatException("日曆 JSON 日期重複或超出涵蓋期間。");
        return file;
    }
    public static void ValidatePair(CalendarFile<Holiday> holidays,CalendarFile<LunarCalendarEntry> lunar)
    {
        if(holidays.Version!=lunar.Version||holidays.From!=lunar.From||holidays.To!=lunar.To)
            throw new FormatException("假日與農曆必須使用相同版本與涵蓋期間，尚未套用任何變更。");
    }
}
