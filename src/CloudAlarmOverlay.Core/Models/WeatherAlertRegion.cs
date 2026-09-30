using System.Globalization;
using System.Text;

namespace CloudAlarmOverlay.Core.Models;

public enum AlertAreaMatch { Outside, Matches, Unknown }

public static class WeatherAlertRegion
{
    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Replace('台', '臺').Where(c => !char.IsWhiteSpace(c) && c is not '/' and not '／'));

    public static TaiwanWeatherDistrict? District(WeatherLocation location) => TaiwanWeatherLocations.All
        .FirstOrDefault(d => Normalize(d.Location.Name) == Normalize(location.Name));

    public static string? County(WeatherLocation location) => District(location)?.County ??
        TaiwanWeatherLocations.Counties.FirstOrDefault(c => Normalize(c) == Normalize(location.Name));

    public static AlertAreaMatch Match(WeatherAlert alert, WeatherLocation location)
    {
        var results = alert.Areas.Select(a => Match(a, location)).ToArray();
        return results.Contains(AlertAreaMatch.Matches) ? AlertAreaMatch.Matches :
            results.Length == 0 || results.Contains(AlertAreaMatch.Unknown) ? AlertAreaMatch.Unknown : AlertAreaMatch.Outside;
    }

    public static AlertAreaMatch Match(WeatherAlertArea area, WeatherLocation location)
    {
        var district = District(location);
        var county = district?.County ?? County(location);
        var results = new List<bool>();
        var unknown = false;
        // Codes take precedence over descriptions: a county mentioned in prose may only affect a few towns.
        var townCodes = area.Codes.Where(code => TaiwanWeatherLocations.All.Any(d => d.CwaTownId == code || d.Code == code)).ToArray();
        foreach (var code in townCodes.Length > 0 ? townCodes : area.Codes)
        {
            var town = TaiwanWeatherLocations.All.FirstOrDefault(d => d.CwaTownId == code || d.Code == code);
            if (town is not null)
            {
                if (district is not null) results.Add(district.Code == town.Code);
                else if (county is not null) results.Add(county == town.County);
                else unknown = true;
                continue;
            }
            var countyDistrict = TaiwanWeatherLocations.All.FirstOrDefault(d =>
                d.Code[..5] == code || (code.Length == 2 && d.Code[..2] == code && code is "63" or "64" or "65" or "66" or "67" or "68"));
            if (countyDistrict is not null && county is not null) results.Add(countyDistrict.County == county);
            else unknown = true;
        }
        // Use the configured forecast reference point only for explicitly supplied geographic areas.
        foreach (var polygon in area.Polygons)
        {
            var contains = InPolygon(polygon, location);
            if (contains is {} value) results.Add(value); else unknown = true;
        }
        foreach (var circle in area.Circles)
        {
            var contains = InCircle(circle, location);
            if (contains is {} value) results.Add(value); else unknown = true;
        }
        if (area.Codes.Count == 0 && area.Polygons.Count == 0 && area.Circles.Count == 0)
        {
            var name = Normalize(area.Name);
            if (name is "全臺" or "全臺灣" or "全國" or "臺灣全島" or "全臺各地") return AlertAreaMatch.Matches;
            // Exact administrative names only. Do not infer coverage from phrases such as 北部山區 or coastal waters.
            var namedTown = TaiwanWeatherLocations.All.FirstOrDefault(d => Normalize(d.Location.Name) == name);
            if (namedTown is not null && county is not null) results.Add(district is not null ? namedTown.Code == district.Code : namedTown.County == county);
            else if (TaiwanWeatherLocations.Counties.FirstOrDefault(c => Normalize(c) == name) is {} namedCounty && county is not null) results.Add(county == namedCounty);
            else unknown = true;
        }
        return results.Contains(true) ? AlertAreaMatch.Matches : unknown ? AlertAreaMatch.Unknown : AlertAreaMatch.Outside;
    }

    private static bool Point(string value, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        var parts = value.Split(',');
        return parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) &&
            double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private static bool? InCircle(string text, WeatherLocation location)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Point(parts[0], out var lat, out var lon) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) || !double.IsFinite(radius) || radius < 0) return null;
        if (radius == 0) return false; // Earthquake epicentre markers are not impact areas.
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var a = Math.Pow(Math.Sin(Rad(location.Latitude - lat) / 2), 2) +
            Math.Cos(Rad(lat)) * Math.Cos(Rad(location.Latitude)) * Math.Pow(Math.Sin(Rad(location.Longitude - lon) / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1))) <= radius;
    }

    private static bool? InPolygon(string text, WeatherLocation location)
    {
        var vertices = new List<(double X, double Y)>();
        foreach (var pair in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Point(pair, out var lat, out var lon)) return null;
            vertices.Add((lon, lat));
        }
        if (vertices.Count < 4 || vertices[0] != vertices[^1]) return null;
        var x = location.Longitude; var y = location.Latitude; var inside = false;
        for (var i = 1; i < vertices.Count; i++)
        {
            var (ax, ay) = vertices[i - 1]; var (bx, by) = vertices[i];
            if (Math.Abs((x - ax) * (by - ay) - (y - ay) * (bx - ax)) < 1e-10 &&
                x >= Math.Min(ax, bx) && x <= Math.Max(ax, bx) && y >= Math.Min(ay, by) && y <= Math.Max(ay, by)) return true;
            if ((ay > y) != (by > y) && x < (bx - ax) * (y - ay) / (by - ay) + ax) inside = !inside;
        }
        return inside;
    }
}
