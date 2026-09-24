using System.Collections.ObjectModel;
using System.Text.Json;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class PreferencesViewModel(ISettingsRepository settings,NotificationPreferences preferences,ISoundService sound,CloudAlarmOverlay.App.Services.EmojiLibrary emojis,MaintenanceViewModel maintenance,ChangeSignal changes,WeatherViewModel? weather=null):ObservableObject
{
    [ObservableProperty] private int selectedSettingsTab;
    public WeatherViewModel? Weather => weather;
    [ObservableProperty] private bool countdownShareBranding=true;
    [ObservableProperty] private string countdownShareMessage="";
    [RelayCommand] private async Task SaveCountdownShareAsync()
    {
        try
        {
            await settings.SaveAsync(new Setting{Key=CountdownShareSnapshot.BrandingSettingKey,Value=CountdownShareBranding?"true":"false"});
            CountdownShareMessage="已儲存，新開啟的分享圖片會使用此設定。";
        }
        catch(Exception ex){CountdownShareMessage=ex.Message;}
    }
    public int[] HomePinLimitChoices {get;}=[2,3,4,5];
    [ObservableProperty] private int homePinLimit=HomePinOptions.DefaultLimit;
    [ObservableProperty] private string homePinMessage="";
    [RelayCommand] private async Task SaveHomePinLimitAsync()
    {
        try
        {
            await settings.SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value=HomePinLimit.ToString()});
            changes.Notify();
            HomePinMessage=$"已儲存：任務與倒數／正數合計最多 {HomePinLimit} 張卡片。";
        }
        catch(Exception ex){HomePinMessage=ex.Message;}
    }
    public MaintenanceViewModel Maintenance {get;}=maintenance;
    public string ApplicationName => "Cloud Alarm Overlay";
    public string ApplicationAuthor => "Bruce Yang";
    public string ApplicationVersion => typeof(PreferencesViewModel).Assembly.GetName().Version?.ToString(3) ?? "未提供";
    public string ApplicationLastUpdated => typeof(PreferencesViewModel).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "AppLastUpdated")?.Value ?? "未提供";
    public ObservableCollection<string> EmojiItems { get; } = [];
    [ObservableProperty] private string newEmoji = "";
    [ObservableProperty] private string? selectedEmoji;
    [ObservableProperty] private string emojiMessage = "";
    private void SetEmojiItems(IEnumerable<string> values)
    {
        EmojiItems.Clear(); foreach(var value in values) EmojiItems.Add(value);
        SelectedEmoji = EmojiItems.FirstOrDefault();
    }
    [RelayCommand] private void AddEmoji()
    {
        var value = NewEmoji.Trim();
        if(value.Length == 0) return;
        if(value.Contains('\n') || value.Contains('\r') || value.Length > 24) { EmojiMessage = "請一次新增一個 emoji，最多 24 字元。"; return; }
        if(EmojiItems.Contains(value)) { SelectedEmoji = value; EmojiMessage = "這個項目已在清單中。"; return; }
        if(EmojiItems.Count >= 60) { EmojiMessage = "最多可放 60 個項目。"; return; }
        EmojiItems.Add(value); SelectedEmoji = value; NewEmoji = ""; EmojiMessage = "已新增，按儲存後套用。";
    }
    [RelayCommand] private void RemoveEmoji()
    {
        if(SelectedEmoji is not {} selected) return;
        var index = EmojiItems.IndexOf(selected); EmojiItems.Remove(selected);
        SelectedEmoji = EmojiItems.Count == 0 ? null : EmojiItems[Math.Min(index, EmojiItems.Count - 1)];
        EmojiMessage = "已移除，按儲存後套用。";
    }
    [RelayCommand] private void MoveEmoji(string direction)
    {
        if(SelectedEmoji is not {} selected) return;
        var index = EmojiItems.IndexOf(selected);
        var target = index + (direction == "left" ? -1 : 1);
        if(index < 0 || target < 0 || target >= EmojiItems.Count) return;
        EmojiItems.Move(index, target); SelectedEmoji = selected; EmojiMessage = "順序已調整，按儲存後套用。";
    }
    [RelayCommand] private async Task SaveEmojisAsync()
    {
        try { await emojis.SaveAsync(string.Join("\n", EmojiItems)); EmojiMessage = "常用 emoji 已儲存。"; }
        catch(Exception ex) { EmojiMessage = ex.Message; }
    }
    [RelayCommand] private void ResetEmojis() { SetEmojiItems(CloudAlarmOverlay.App.Services.EmojiLibrary.Defaults.Split('\n')); EmojiMessage = "已恢復預設，按儲存後套用。"; }
    [ObservableProperty] private int flashMilliseconds=500;
    [ObservableProperty] private bool flashLocked;
    [ObservableProperty] private bool quietLocked;
    [ObservableProperty] private string message="";
    public bool CanEditFlash=>!FlashLocked;
    public bool CanEditQuiet=>!QuietLocked;
    public string FlashLockLabel=>FlashLocked?"🔒 閃爍間隔（毫秒）· 管理者已鎖定":"閃爍間隔（毫秒）";
    public string QuietLockLabel=>QuietLocked?"🔒 靜音時段 · 管理者已鎖定":"靜音時段";
    public int[] FlashChoices {get;}=[200,500,800,1000,2000,5000];
    public ObservableCollection<QuietPeriodRow> QuietPeriods {get;}=[];
    public ObservableCollection<SoundRow> Sounds {get;}=[];
    public async Task LoadAsync()
    {
        CountdownShareBranding=(await settings.GetAsync(CountdownShareSnapshot.BrandingSettingKey))?.Value!="false";
        if (Weather is not null) await Weather.LoadAsync();
        HomePinLimit=HomePinOptions.ReadLimit((await settings.GetAsync(HomePinOptions.SettingKey))?.Value);
        await emojis.LoadAsync(); SetEmojiItems(emojis.Items); EmojiMessage = "";
        FlashMilliseconds=await preferences.FlashMillisecondsAsync();
        FlashLocked=(await settings.GetAsync("FlashMilliseconds"))?.Locked??false;
        QuietLocked=(await settings.GetAsync("QuietPeriods"))?.Locked??false;
        QuietPeriods.Clear();
        foreach(var p in await preferences.QuietPeriodsAsync())QuietPeriods.Add(new(){Start=p.Start,End=p.End});
        Sounds.Clear();
        var available=sound.GetAvailableSounds();
        foreach(var level in new[]{AlarmLevels.Low,AlarmLevels.Mid,AlarmLevels.High,AlarmLevels.Max})
        {
            var p=await preferences.SoundAsync(level);
            var row=new SoundRow{Level=level,Enabled=p.Enabled,Name=p.Name,Available=available};
            row.PropertyChanged+=async(_,e)=>{
                if(e.PropertyName is nameof(SoundRow.Enabled) or nameof(SoundRow.Name))
                    try{await settings.SaveAsync(new Setting{Key="Sound:"+row.Level,Value=JsonSerializer.Serialize(new SoundPreference(row.Enabled,row.Name))});}
                    catch(Exception ex){Message=ex.Message;}
            };
            Sounds.Add(row);
        }
        foreach(var name in new[]{nameof(CanEditFlash),nameof(CanEditQuiet),nameof(FlashLockLabel),nameof(QuietLockLabel)})OnPropertyChanged(name);
    }
    [RelayCommand] private void AddQuiet(){if(CanEditQuiet)QuietPeriods.Add(new());}
    [RelayCommand] private void RemoveQuiet(QuietPeriodRow row){if(CanEditQuiet)QuietPeriods.Remove(row);}
    [RelayCommand] private async Task SaveAsync()
    {
        try
        {
            var quiet=new Setting{Key="QuietPeriods",Value=JsonSerializer.Serialize(QuietPeriods.Select(r=>new QuietPeriod(r.Start,r.End)))};
            var flash=new Setting{Key="FlashMilliseconds",Value=FlashMilliseconds.ToString()};
            if(CanEditQuiet)NotificationPreferences.Validate(quiet);
            if(CanEditFlash)NotificationPreferences.Validate(flash);
            if(CanEditQuiet)await settings.SaveAsync(quiet);
            if(CanEditFlash)await settings.SaveAsync(flash);
            Message="偏好設定已儲存。";
        }
        catch(Exception ex){Message=ex.Message;}
    }
    [RelayCommand] private async Task PreviewSoundAsync(SoundRow row)
    {
        if(row.IsPreviewing)return;
        row.IsPreviewing=true;
        row.PreviewStatus="正在試聽…";
        try
        {
            if(string.IsNullOrWhiteSpace(row.Name))throw new InvalidOperationException("請先選擇要試聽的聲音。");
            await sound.PlayAsync(row.Name);
            row.PreviewStatus="已執行試聽；若無聲請檢查 Windows 靜音與音量。";
            await Task.Delay(1500);
        }
        catch(Exception ex){row.PreviewStatus="試聽失敗："+ex.Message;Message=ex.Message;}
        finally{row.IsPreviewing=false;}
    }
}
public partial class QuietPeriodRow:ObservableObject
{
    [ObservableProperty] private string start="12:00";
    [ObservableProperty] private string end="13:00";
}
public partial class SoundRow:ObservableObject
{
    public required string Level {get;init;}
    [ObservableProperty] private bool isPreviewing;
    [ObservableProperty] private string previewStatus="";
    public string PreviewButtonLabel=>IsPreviewing?"◉ 試聽中":"▷ 試聽";
    partial void OnIsPreviewingChanged(bool value)=>OnPropertyChanged(nameof(PreviewButtonLabel));
    partial void OnPreviewStatusChanged(string value)=>OnPropertyChanged(nameof(Note));
    public string Note=>PreviewStatus.Length>0?PreviewStatus:!HasSounds?"未找到可用的 Windows 音效。":Level switch
    {
        AlarmLevels.Low=>"日常事項，短暫提示。",
        AlarmLevels.Mid=>"重要待辦，需要確認。",
        AlarmLevels.High=>"緊急事項，請即時處理。",
        _=>"強制通知仍會顯示；音效可自行關閉。"
    };
    public IReadOnlyList<string> Available {get;init;}=[];
    public bool HasSounds=>Available.Count>0;
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string name="";
}
