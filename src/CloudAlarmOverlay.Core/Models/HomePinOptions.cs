namespace CloudAlarmOverlay.Core.Models;

public static class HomePinOptions
{
    public const string SettingKey = "HomePinLimit";
    public const int DefaultLimit = 2;
    public const int MaximumLimit = 5;
    public static int ReadLimit(string? value) => int.TryParse(value, out var limit) && limit is >= DefaultLimit and <= MaximumLimit ? limit : DefaultLimit;
    public static void Validate(string? value)
    {
        if (!int.TryParse(value, out var limit) || limit is < DefaultLimit or > MaximumLimit)
            throw new ArgumentException("首頁釘選上限請選擇 2～5 張卡片。");
    }
    public static string FullMessage(int limit) => $"首頁最多釘選 {limit} 項，任務與倒數／正數共用名額，請先取消其他釘選。";
}
