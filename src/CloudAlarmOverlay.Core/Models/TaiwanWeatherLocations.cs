using System.Text.Json;
using System.Text;
namespace CloudAlarmOverlay.Core.Models;

public sealed record TaiwanWeatherDistrict(string Code, string County, string District, double Latitude, double Longitude, string PostalCode, string ReferenceName, string ReferenceSource)
{
    public string SearchLabel => $"{PostalCode}　{County}／{District}";
    public string DistrictLabel => $"{District}（{PostalCode}）";
    public override string ToString() => DistrictLabel;
    public WeatherLocation Location => new($"{County}／{District}", Latitude, Longitude);
    // CWA Info_Town.js uses seven-digit IDs, distinct from MOI's eight-digit codes.
    public string CwaTownId => Code[..2] is "63" or "64" or "65" or "66" or "67" or "68"
        ? Code[..2] + Code[4..7] + "00" : Code[..7];
}

public static class TaiwanWeatherLocations
{
    public static IReadOnlyList<TaiwanWeatherDistrict> All { get; } = Load();
    public static IReadOnlyList<string> Counties { get; } = Array.AsReadOnly(new[]
    {
        "基隆市", "臺北市", "新北市", "桃園市", "新竹市", "新竹縣", "苗栗縣", "臺中市", "彰化縣", "南投縣", "雲林縣",
        "嘉義市", "嘉義縣", "臺南市", "高雄市", "屏東縣", "宜蘭縣", "花蓮縣", "臺東縣", "澎湖縣", "金門縣", "連江縣"
    });
    public static IReadOnlyList<TaiwanWeatherDistrict> Search(string? query)
    {
        var text = Normalize(query ?? "");
        if (text.Length == 0) return [];
        var numeric = text.Replace("-", "").Replace(" ", "");
        if (numeric.All(c => c is >= '0' and <= '9'))
        {
            if (numeric.Length is 4 or > 6) return [];
            var prefix = numeric.Length >= 3 ? numeric[..3] : numeric;
            return All.Where(d => d.PostalCode.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        }
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return All.Where(d => parts.All(part =>
            Normalize(d.County + d.District).Contains(part, StringComparison.Ordinal) ||
            Normalize(d.County[..^1] + d.District).Contains(part, StringComparison.Ordinal) ||
            d.PostalCode == part)).ToArray();
    }
    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).Trim().Replace('台', '臺')
        .Replace('／', ' ').Replace('/', ' ').Replace('　', ' ').Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static IReadOnlyList<TaiwanWeatherDistrict> Load()
    {
        using var stream = typeof(TaiwanWeatherLocations).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Core.Data.TaiwanWeatherLocations.json")
            ?? throw new InvalidOperationException("找不到內建天氣地點清單。");
        return Array.AsReadOnly(JsonSerializer.Deserialize<TaiwanWeatherDistrict[]>(stream)!);
    }
}
