using System.Text.Json;
using System.Text.Json.Serialization;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

public static class AdminSettingsJson
{
    public const int MaximumBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public const string Template = """
        {
          "FormatVersion": 1,
          "SyncOptions": {
            "SheetAId": "REPLACE_WITH_SHEET_A_ID",
            "TasksAGid": "0",
            "HolidaysGid": "1",
            "EmployeesGid": "2",
            "LunarGid": "3",
            "SheetBId": "",
            "TasksBGid": "",
            "IntervalSeconds": 45
          },
          "SyncLinksLocked": false,
          "AllowUrgentSnooze": true,
          "FlashMilliseconds": 500,
          "LockFlash": false,
          "QuietPeriods": [],
          "LockQuiet": false,
          "WeatherDefaultDistrictCode": "65000010"
        }
        """;

    public static IReadOnlyList<Setting> Parse(string text)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaximumBytes)
            throw new ArgumentException("設定檔上限為 64 KB。");
        try
        {
            using var document = JsonDocument.Parse(text);
            CheckProperties(document.RootElement);
            var input = JsonSerializer.Deserialize<SettingsFile>(text, Options)
                ?? throw new ArgumentException("設定檔不能為空。");
            if (input.FormatVersion != 1) throw new ArgumentException("FormatVersion 必須為 1。");
            var result = new List<Setting>();
            void Add(string key, string value, bool locked = false) => result.Add(new() { Key = key, Value = value, Locked = locked });
            if (input.SyncOptions is { } sync)
            {
                var group = document.RootElement.GetProperty("SyncOptions");
                foreach (var name in new[] { "SheetAId", "TasksAGid", "HolidaysGid", "EmployeesGid", "LunarGid", "SheetBId", "TasksBGid", "IntervalSeconds" })
                    if (!group.TryGetProperty(name, out _)) throw new ArgumentException("SyncOptions 需提供完整八個欄位，請參考範本。");
                sync.Validate();
                Add("SyncOptions", JsonSerializer.Serialize(sync));
            }
            if (input.UpdateManifestUrl is { } url)
            {
                if (!string.IsNullOrWhiteSpace(url)) UpdateCheckService.ValidateUrl(url.Trim());
                Add("UpdateManifestUrl", url.Trim());
            }
            if (input.SyncLinksLocked is { } links) Add("SyncLinksLocked", links ? "true" : "false");
            if (input.AllowUrgentSnooze is { } snooze) Add("AllowUrgentSnooze", snooze ? "true" : "false", true);
            if ((input.LockFlash is null) != (input.FlashMilliseconds is null)) throw new ArgumentException("LockFlash 需與 FlashMilliseconds 一起提供。");
            if (input.FlashMilliseconds is { } flash) Add("FlashMilliseconds", flash.ToString(System.Globalization.CultureInfo.InvariantCulture), input.LockFlash ?? false);
            if ((input.LockQuiet is null) != (input.QuietPeriods is null)) throw new ArgumentException("LockQuiet 需與 QuietPeriods 一起提供。");
            if (input.QuietPeriods is { } quiet) Add("QuietPeriods", JsonSerializer.Serialize(quiet), input.LockQuiet ?? false);
            if (input.WeatherDefaultDistrictCode is { } code)
            {
                var district = TaiwanWeatherLocations.All.SingleOrDefault(d => d.Code == code)
                    ?? throw new ArgumentException("WeatherDefaultDistrictCode 必須是內建鄉鎮市區代碼。");
                Add("WeatherDefaultLocation", JsonSerializer.Serialize(district.Location));
            }
            if (result.Count == 0) throw new ArgumentException("設定檔未提供任何可匯入項目。");
            foreach (var setting in result) NotificationPreferences.Validate(setting);
            return result;
        }
        catch (JsonException) { throw new ArgumentException("JSON 格式或欄位不正確，請使用設定範本；不接受未知欄位或密碼。"); }
        catch (FormatException) { throw new ArgumentException("設定值格式錯誤，請檢查時間等欄位。"); }
    }

    private static void CheckProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || property.Value.ValueKind == JsonValueKind.Null)
                    throw new ArgumentException("設定檔不接受重複欄位或 null；不需修改的欄位請省略。");
                CheckProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) throw new ArgumentException("設定陣列不可包含 null。");
                CheckProperties(item);
            }
    }

    private sealed record SettingsFile
    {
        public required int FormatVersion { get; init; }
        public SyncOptions? SyncOptions { get; init; }
        public string? UpdateManifestUrl { get; init; }
        public bool? SyncLinksLocked { get; init; }
        public bool? AllowUrgentSnooze { get; init; }
        public int? FlashMilliseconds { get; init; }
        public bool? LockFlash { get; init; }
        public QuietPeriod[]? QuietPeriods { get; init; }
        public bool? LockQuiet { get; init; }
        public string? WeatherDefaultDistrictCode { get; init; }
    }
}
