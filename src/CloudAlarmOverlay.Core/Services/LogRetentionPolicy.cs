using System.Globalization;

namespace CloudAlarmOverlay.Core.Services;

public static class LogRetentionPolicy
{
    public const string Key = "RuntimeLogRetentionDays";
    public const int DefaultDays = 30;
    public static int Validate(string? value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days is < 1 or > 365)
            throw new ArgumentException("日誌保留天數必須為 1–365 的整數。");
        return days;
    }
    public static int Read(string? value)
    {
        try { return Validate(value); }
        catch (ArgumentException) { return DefaultDays; }
    }
}
