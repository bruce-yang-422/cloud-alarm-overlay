using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.App.ViewModels;

public sealed record WeatherAlertBadge(WeatherAlertKind Kind, string Label, string Details)
{
    public Uri OfficialUri => new(Kind == WeatherAlertKind.Earthquake
        ? "https://scweb.cwa.gov.tw/" : "https://www.cwa.gov.tw/V8/C/P/Warning/FIFOWS.html");

    // Small, monochrome vector icons remain legible at different display scales.
    public string IconPath => Kind switch
    {
        WeatherAlertKind.Earthquake => "M1,12 L5,12 8,4 11,20 15,8 18,12 23,12",
        WeatherAlertKind.Typhoon => "M18,3 C10,0 3,7 5,14 C7,20 16,21 19,14 C22,7 13,2 8,7 C3,12 7,23 16,22 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12",
        WeatherAlertKind.Tsunami => "M2,15 C5,15 5,4 13,4 C19,4 20,9 17,11 C18,5 10,7 13,13 C15,17 20,17 22,17 M2,21 Q5,17 8,21 Q11,17 14,21 Q17,17 22,21",
        WeatherAlertKind.Rain => "M5,13 C0,13 1,6 6,7 C7,0 18,1 18,7 C24,6 25,13 20,13 Z M7,17 L5,21 M13,17 L11,21 M19,17 L17,21",
        WeatherAlertKind.Thunderstorm => "M5,12 C0,12 1,5 6,6 C8,0 18,1 18,6 C24,5 25,12 20,12 M13,10 L8,17 13,17 10,24 19,14 14,14 17,10 Z",
        WeatherAlertKind.Wind => "M2,7 L16,7 C23,7 23,0 17,2 M2,12 L20,12 M2,17 L14,17 C21,17 21,24 15,22",
        WeatherAlertKind.Heat => "M9,14 L9,4 A3,3 0 0 1 15,4 L15,14 A5,5 0 1 1 9,14 M12,9 L12,18 M19,4 L23,4 M19,8 L22,8",
        WeatherAlertKind.Cold => "M12,2 L12,22 M3,7 L21,17 M3,17 L21,7 M8,3 L12,7 16,3 M8,21 L12,17 16,21 M3,11 L7,9 7,5 M17,19 L17,15 21,13",
        WeatherAlertKind.Fog => "M5,10 C0,10 2,3 7,5 C10,0 19,2 19,7 M2,13 L22,13 M4,17 L20,17 M2,21 L22,21",
        _ => "M12,2 L23,22 1,22 Z M12,9 L12,15 M12,18 L12,19"
    };

    public static WeatherAlertBadge Create(IGrouping<WeatherAlertKind, WeatherAlert> group, DateTimeOffset checkedAt, WeatherLocation location)
    {
        var items = group.OrderByDescending(a => a.PublishedAt).ToArray();
        var label = group.Key switch
        {
            WeatherAlertKind.Earthquake => "地震報告", WeatherAlertKind.Typhoon => "颱風",
            WeatherAlertKind.Tsunami => "海嘯", WeatherAlertKind.Rain => "豪大雨",
            WeatherAlertKind.Thunderstorm => "雷雨", WeatherAlertKind.Wind => "強風",
            WeatherAlertKind.Heat => "高溫", WeatherAlertKind.Cold => "低溫",
            WeatherAlertKind.Fog => "濃霧", _ => "其他特報"
        };
        var details = string.Join("\n\n", items.Take(3).Select(a =>
            $"{a.Title} · {a.PublishedAt.ToOffset(TimeSpan.FromHours(8)):MM/dd HH:mm} 發布\n" +
            (a.SeverityLabel.Length > 0 ? a.SeverityLabel + "\n" : "") +
            "符合範圍：" + string.Join("、", a.Areas.Where(area => WeatherAlertRegion.Match(area, location) == AlertAreaMatch.Matches)
                .Select(area => area.Name.Length > 0 ? area.Name : "官方地理範圍").Distinct().Take(4)) + $"\n{a.Summary}"));
        if (items.Length > 3) details += $"\n\n另有 {items.Length - 3} 則，請至官方查看。";
        details += $"\n\n篩選地點：{location.Name}" + (WeatherAlertRegion.District(location) is null && WeatherAlertRegion.County(location) is not null ? "（縣市篩選）" : "") +
            $"\n依官方影響範圍比對，詳細內容以公告為準。\n中央氣象署 · NCDR 示警平台\n查詢：{checkedAt.ToOffset(TimeSpan.FromHours(8)):MM/dd HH:mm}（臺灣時間）\n點擊查看官方資訊。";
        return new(group.Key, label, details);
    }
}
