using System.Globalization;
using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Infrastructure;

public sealed class OpenMeteoWeatherClient(HttpClient http)
{
    private async Task<JsonDocument> ReadAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return JsonDocument.Parse(await http.GetStringAsync(url, timeout.Token));
    }
    public async Task<IReadOnlyList<WeatherLocation>> SearchAsync(string city, CancellationToken ct)
    {
        if (city.Trim().Length < 2) throw new ArgumentException("請至少輸入兩個字搜尋城市。");
        using var json = await ReadAsync("https://geocoding-api.open-meteo.com/v1/search?count=10&language=zh&format=json&name=" + Uri.EscapeDataString(city.Trim()), ct);
        if (!json.RootElement.TryGetProperty("results", out var rows)) return [];
        return rows.EnumerateArray().Select(r => new WeatherLocation(
            string.Join(" · ", new[] { r.GetProperty("name").GetString(), r.TryGetProperty("admin1", out var a) ? a.GetString() : null, r.TryGetProperty("country", out var c) ? c.GetString() : null }.Where(s => !string.IsNullOrEmpty(s)).Distinct()),
            r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble())).ToArray();
    }
    public async Task<WeatherSnapshot> FetchAsync(WeatherLocation location, DateTimeOffset now, CancellationToken ct)
    {
        location.Validate();
        var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude}&longitude={location.Longitude}&current=temperature_2m,apparent_temperature,weather_code,precipitation&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=Asia%2FTaipei&forecast_days=2");
        using var json = await ReadAsync(url, ct);
        return Parse(json.RootElement, location, now);
    }
    public static WeatherSnapshot Parse(JsonElement root, WeatherLocation location, DateTimeOffset now)
    {
        var current = root.GetProperty("current"); var daily = root.GetProperty("daily");
        WeatherDay Day(int i) => new(DateOnly.ParseExact(daily.GetProperty("time")[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            daily.GetProperty("weather_code")[i].GetInt32(), daily.GetProperty("temperature_2m_min")[i].GetDouble(),
            daily.GetProperty("temperature_2m_max")[i].GetDouble(), daily.GetProperty("precipitation_probability_max")[i].GetInt32());
        var today = Day(0); var tomorrow = Day(1);
        if (tomorrow.Date != today.Date.AddDays(1) || today.RainProbability is < 0 or > 100 || tomorrow.RainProbability is < 0 or > 100)
            throw new FormatException("天氣預報日期或降雨機率無效。");
        return new(location, now, DateTime.Parse(current.GetProperty("time").GetString()!, CultureInfo.InvariantCulture),
            current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("apparent_temperature").GetDouble(),
            current.GetProperty("precipitation").GetDouble(), current.GetProperty("weather_code").GetInt32(), today, tomorrow);
    }
}
