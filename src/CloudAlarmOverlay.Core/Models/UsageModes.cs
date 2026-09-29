namespace CloudAlarmOverlay.Core.Models;

public static class UsageModes
{
    public const string Key = "UsageMode";
    public const string Personal = "Personal";
    public const string Company = "Company";
    public static void Validate(string mode)
    {
        if (mode is not (Personal or Company)) throw new ArgumentException("請選擇個人使用或公司使用。");
    }
}
