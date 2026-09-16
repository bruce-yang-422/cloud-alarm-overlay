using System.Globalization;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Recurrence;
using CsvHelper;
using CsvHelper.Configuration;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class CsvSheetParser : ICsvSheetParser
{
    private static List<Dictionary<string,string>> Read(string text,params string[] required)
    {
        using var input = new StringReader(text.TrimStart('\uFEFF'));
        using var csv = new CsvReader(input,new CsvConfiguration(CultureInfo.InvariantCulture){TrimOptions=TrimOptions.Trim,IgnoreBlankLines=false});
        if(!csv.Read()) throw new FormatException("CSV 缺少標題列。");
        csv.ReadHeader();
        var headers=csv.HeaderRecord!;
        if(headers.Distinct(StringComparer.Ordinal).Count()!=headers.Length) throw new FormatException("CSV 標題重複。");
        foreach(var name in required) if(!headers.Contains(name)) throw new FormatException($"CSV 缺少必要欄位：{name}");
        if(!csv.Read()) throw new FormatException("CSV 缺少第二列欄位說明。");
        var rows=new List<Dictionary<string,string>>();
        while(csv.Read())
        {
            var row=headers.ToDictionary(h=>h,h=>csv.GetField(h)?.Trim()??"");
            if(row.Values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(row);
        }
        return rows;
    }
    private static string Get(Dictionary<string,string> row,string key,string fallback="")=>row.GetValueOrDefault(key,fallback);
    private static bool Bool(string value,bool fallback=false)=>value.ToUpperInvariant() switch{
        "" or "-" => fallback,"TRUE" or "是"=>true,"FALSE" or "否"=>false,_=>throw new FormatException($"無效的布林值：{value}")};
    private static void Unique(IEnumerable<string> ids)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var id in ids) if(string.IsNullOrWhiteSpace(id)||!seen.Add(id)) throw new FormatException($"編號空白或重複：{id}");
    }
    public IReadOnlyList<AlarmTask> ParseTasks(string csv,string source)
    {
        if(source is not (TaskSources.SheetA or TaskSources.SheetB)) throw new ArgumentException("來源錯誤。");
        var now=DateTime.Now;
        var result=Read(csv,"Id","Time","Title","Enabled").Select(row=>{
            if(!DateTime.TryParseExact(Get(row,"Time"),new[]{"yyyy-MM-dd HH:mm","yyyy-MM-dd HH:mm:ss","yyyy-MM-ddTHH:mm:ss"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var at))
                throw new FormatException($"任務 {Get(row,"Id")} 時間格式錯誤。");
            var title=Get(row,"Title"); if(title.Length is <1 or >50) throw new FormatException("任務名稱需為 1–50 字。");
            var level=AlarmLevels.Normalize(Get(row,"Level",AlarmLevels.Mid)); if(level=="")level=AlarmLevels.Mid;
            if(level is not (AlarmLevels.Low or AlarmLevels.Mid or AlarmLevels.High or AlarmLevels.Max)) throw new FormatException($"無效等級：{level}");
            return new AlarmTask{Id=source+":"+Get(row,"Id"),ExternalId=Get(row,"Id"),Title=title,Description=Get(row,"Description"),
                ScheduledAt=at,Level=level,Enabled=Bool(Get(row,"Enabled")),RequireAcknowledgement=Bool(Get(row,"RequireAck"),level!=AlarmLevels.Low),
                Source=source,Recurrence=RecurrenceRule.NormalizeCsv(Get(row,"Recurrence"),at),SkipOnHoliday=Bool(Get(row,"SkipOnHoliday")),
                TargetDeviceOrName=Get(row,"TargetDeviceOrName"),ExcludeDeviceOrName=Get(row,"ExcludeDeviceOrName"),CreatedAt=now,UpdatedAt=now};
        }).ToArray();
        Unique(result.Select(t=>t.ExternalId!)); return result;
    }
    public IReadOnlyList<Holiday> ParseHolidays(string csv)
    {
        var result=Read(csv,"Date","Type").Select(r=>new Holiday{Date=DateOnly.ParseExact(Get(r,"Date"),"yyyy-MM-dd",CultureInfo.InvariantCulture),Type=Get(r,"Type"),Note=Get(r,"Note")}).ToArray();
        if(result.Any(h=>h.Type is not ("國定假日" or "全公司停班" or "補班日" or "其他"))) throw new FormatException("假日類型無效。");
        Unique(result.Select(x=>x.Date.ToString("O"))); return result;
    }
    public IReadOnlyList<Employee> ParseEmployees(string csv)
    {
        var result=Read(csv,"Id").Select(r=>{
            var max=AlarmLevels.Normalize(Get(r,"MaxAllowedLevel"));
            if(max is not ("" or "-" or AlarmLevels.Low or AlarmLevels.Mid or AlarmLevels.High or AlarmLevels.Max))throw new FormatException("員工等級上限無效。");
            var ack=Get(r,"RequireAckOverride");
            return new Employee{DeviceId=Get(r,"Id"),Name=Get(r,"Name"),Department=Get(r,"Department"),
                MaxAllowedLevel=max is "" or "-"?null:max,RequireAckOverride=ack is "" or "-"?null:Bool(ack)?"TRUE":"FALSE"};
        }).ToArray(); Unique(result.Select(e=>e.DeviceId)); return result;
    }
    public IReadOnlyList<LunarCalendarEntry> ParseLunarCalendar(string csv)
    {
        var result=Read(csv,"Date","LunarDay").Select(r=>new LunarCalendarEntry{Date=DateOnly.ParseExact(Get(r,"Date"),"yyyy-MM-dd",CultureInfo.InvariantCulture),
            LunarDay=int.Parse(Get(r,"LunarDay"),CultureInfo.InvariantCulture),LunarDate=Get(r,"LunarDate"),SolarTerm=Get(r,"SolarTerm")}).ToArray();
        if(result.Any(e=>e.LunarDay is <1 or >30))throw new FormatException("農曆日期必須介於 1–30。");
        Unique(result.Select(x=>x.Date.ToString("O"))); return result;
    }
}
