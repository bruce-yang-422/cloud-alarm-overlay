using System.Globalization;
using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
public sealed record QuietPeriod(string Start,string End)
{
    public TimeOnly StartTime=>TimeOnly.ParseExact(Start,"HH:mm",CultureInfo.InvariantCulture);
    public TimeOnly EndTime=>TimeOnly.ParseExact(End,"HH:mm",CultureInfo.InvariantCulture);
    public bool Contains(TimeOnly now)=>StartTime<EndTime?now>=StartTime&&now<EndTime:now>=StartTime||now<EndTime;
}
public sealed record SoundPreference(bool Enabled=false,string Name="");
public sealed class NotificationPreferences(ISettingsRepository settings)
{
    public async Task<string> ColorModeAsync(CancellationToken ct=default)=>(await settings.GetAsync("NotificationColorMode",ct))?.Value=="暗色"?"暗色":"亮色";
    public async Task<string> ColorSchemeAsync(CancellationToken ct=default)=>(await settings.GetAsync("NotificationColorScheme",ct))?.Value??"依提醒等級";
    public async Task<int> FlashMillisecondsAsync(CancellationToken ct=default)=>
        int.TryParse((await settings.GetAsync("FlashMilliseconds",ct))?.Value,out var ms)?Math.Clamp(ms,200,5000):500;
    public async Task<IReadOnlyList<QuietPeriod>> QuietPeriodsAsync(CancellationToken ct=default)=>
        JsonSerializer.Deserialize<List<QuietPeriod>>((await settings.GetAsync("QuietPeriods",ct))?.Value??"[]")??[];
    public async Task<bool> IsQuietAsync(string level,DateTime now,CancellationToken ct=default)=>
        level!=AlarmLevels.Max&&(await QuietPeriodsAsync(ct)).Any(p=>p.Contains(TimeOnly.FromDateTime(now)));
    public async Task<SoundPreference> SoundAsync(string level,CancellationToken ct=default)=>
        JsonSerializer.Deserialize<SoundPreference>((await settings.GetAsync("Sound:"+level,ct))?.Value??"{}")??new();
    public static void Validate(Setting setting)
    {
        if(setting.Key==HomePinOptions.SettingKey)HomePinOptions.Validate(setting.Value);
        if(setting.Key=="NotificationColorMode"&&setting.Value is not ("亮色" or "暗色"))throw new ArgumentException("請選擇亮色或暗色通知。");
        if(setting.Key=="NotificationColorScheme"&&setting.Value is not ("依提醒等級" or "海灣藍" or "森林綠" or "暮紫"))throw new ArgumentException("請選擇通知配色。");
        if(setting.Key=="FlashMilliseconds"&&(!int.TryParse(setting.Value,out var ms)||ms is <200 or >5000))
            throw new ArgumentException("閃爍間隔需介於 200–5000 毫秒。");
        if(setting.Key=="QuietPeriods")
        {
            var periods=JsonSerializer.Deserialize<List<QuietPeriod>>(setting.Value??"[]")??[];
            if(periods.Count>12)throw new ArgumentException("靜音時段最多 12 組。");
            foreach(var p in periods)if(p.StartTime==p.EndTime)throw new ArgumentException("靜音開始與結束時間不可相同。");
        }
        if(setting.Key.StartsWith("Sound:",StringComparison.Ordinal)&&setting.Locked)throw new ArgumentException("音效不可被鎖定。");
    }
}
