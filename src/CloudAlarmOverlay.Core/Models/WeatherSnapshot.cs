namespace CloudAlarmOverlay.Core.Models;

public sealed record WeatherLocation(string Name, double Latitude, double Longitude)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 200 || !double.IsFinite(Latitude) || !double.IsFinite(Longitude) || Latitude is < -90 or > 90 || Longitude is < -180 or > 180)
            throw new ArgumentException("天氣地點無效。");
    }
    public override string ToString() => Name;
}
public sealed record WeatherOptions(bool Enabled = true, WeatherLocation? Location = null);
public sealed record WeatherDay(DateOnly Date, int Code, double Minimum, double Maximum, int RainProbability);
public sealed record WeatherSnapshot(WeatherLocation Location, DateTimeOffset UpdatedAt, DateTime ObservedAt,
    double Temperature, double ApparentTemperature, double Precipitation, int Code, WeatherDay Today, WeatherDay Tomorrow);
public static class WeatherCodes
{
    public static (string Icon, string Description) Describe(int code) => code switch
    {
        0 => ("☀️", "晴天"), 1 => ("🌤️", "大致晴朗"), 2 => ("⛅", "局部多雲"), 3 => ("☁️", "陰天"),
        45 or 48 => ("🌫️", "霧"), 51 or 53 or 55 => ("🌦️", "毛毛雨"), 56 or 57 => ("🌧️", "凍毛毛雨"),
        61 or 63 or 65 => ("🌧️", "下雨"), 66 or 67 => ("🌧️", "凍雨"),
        71 or 73 or 75 or 77 => ("🌨️", "降雪"), 80 or 81 or 82 => ("🌦️", "陣雨"),
        85 or 86 => ("🌨️", "陣雪"), 95 => ("⛈️", "雷雨"), 96 or 99 => ("⛈️", "雷雨伴隨冰雹"),
        _ => ("？", "未知天氣")
    };
}
