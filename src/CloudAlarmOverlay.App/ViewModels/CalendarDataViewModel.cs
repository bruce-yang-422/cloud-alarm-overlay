using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.ViewModels;

public partial class CalendarDataViewModel(CalendarDataService calendar):ObservableObject
{
    public event Action? SettingsLoaded;
    public Func<bool>? HasPendingChanges { get; set; }
    public async Task OpenAsync()
    {
        // Reopening after session expiry must not silently replace a user's draft.
        UrlsLocked=true;
        if(HasPendingChanges?.Invoke()!=true)await LoadAsync();
    }
    [ObservableProperty] private bool enabled;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FrequencyDescription))] private string frequency="每天";
    [ObservableProperty] private string holidaysUrl="";
    [ObservableProperty] private string lunarUrl="";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrlsReadOnly))]
    [NotifyPropertyChangedFor(nameof(UrlLockDescription))]
    private bool urlsLocked=true;
    [ObservableProperty] private string summary="正在載入內建資料…";
    [ObservableProperty] private string updateStatus="";
    [ObservableProperty] private string message="";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrlsReadOnly))]
    [NotifyPropertyChangedFor(nameof(CanEditSourceSettings))]
    [NotifyCanExecuteChangedFor(nameof(RestoreDefaultUrlsCommand))]
    private bool busy;
    public bool UrlsReadOnly=>UrlsLocked||Busy;
    public bool CanEditSourceSettings=>!Busy;
    public string UrlLockDescription=>UrlsLocked?"網址已鎖定，仍可選取與複製；先關閉鎖定才能編輯。":"網址可編輯；儲存設定或重新開啟此頁後會自動鎖定。";
    public string[] Frequencies { get; }=["每天","每週","每月","手動更新"];
    public string FrequencyDescription=>Frequency switch
    {
        "每週"=>"每週一後首次執行時更新；程式持續開啟時也會檢查。",
        "每月"=>"每月 1 日後首次執行時更新；程式持續開啟時也會檢查。",
        "手動更新"=>"只在按下「立即更新」時連線，不會定期更新。",
        _=>"每天首次執行時更新；程式持續開啟時也會檢查。"
    };
    public async Task LoadAsync()
    {
        try
        {
            await calendar.InitializeAsync();var state=await calendar.GetAsync();
            Enabled=state.Options.Enabled;Frequency=state.Options.Frequency switch{"Weekly"=>"每週","Monthly"=>"每月","Manual"=>"手動更新",_=>"每天"};
            HolidaysUrl=state.Options.HolidaysUrl;LunarUrl=state.Options.LunarUrl;UrlsLocked=true;Render(state);
            SettingsLoaded?.Invoke();
        }
        catch(Exception ex){Message="載入失敗："+ex.Message;}
    }
    private CalendarUpdateOptions Draft()=>new(){Enabled=Enabled,Frequency=Frequency switch{"每天"=>"Daily","每週"=>"Weekly","每月"=>"Monthly","手動更新"=>"Manual",_=>throw new ArgumentException("請選擇更新頻率。")},HolidaysUrl=CalendarUpdateOptions.NormalizeUrl(HolidaysUrl),LunarUrl=CalendarUpdateOptions.NormalizeUrl(LunarUrl)};
    [RelayCommand(CanExecute=nameof(CanEditSourceSettings))] private void RestoreDefaultUrls()
    {
        var defaults=new CalendarUpdateOptions();
        HolidaysUrl=defaults.HolidaysUrl;LunarUrl=defaults.LunarUrl;UrlsLocked=true;
        Message="已還原預設網址並重新鎖定；請按「儲存設定」套用。";
    }
    private void Render(CalendarDataState state)
    {
        Summary=$"{(state.Source=="GitHub"?"GitHub 資料":"安裝包內建資料")} · 版本 {state.Lunar?.Version}\n農曆 {state.Lunar?.Entries.Length??0} 筆 · 假日 {state.Holidays?.Entries.Length??0} 筆\n涵蓋 {state.Lunar?.From:yyyy-MM-dd} ～ {state.Lunar?.To:yyyy-MM-dd}";
        if(state.Lunar is {} lunar&&(DateOnly.FromDateTime(calendar.Now.Date)<lunar.From||DateOnly.FromDateTime(calendar.Now.Date)>lunar.To))Summary+="\n目前日期不在資料涵蓋範圍，請更新日曆資料。";
        UpdateStatus=$"{state.LastResult}\n上次嘗試：{state.LastAttemptAt?.ToString("yyyy-MM-dd HH:mm")??"尚未更新"}\n上次成功：{state.LastSuccessAt?.ToString("yyyy-MM-dd HH:mm")??"尚未更新"}";
    }
    [RelayCommand] private async Task SaveAsync()
    {
        if(Busy)return;Busy=true;
        try{await calendar.SaveOptionsAsync(Draft());await LoadAsync();Message="日曆更新設定已儲存。";}
        catch(Exception ex){Message=ex.Message;}finally{Busy=false;}
    }
    public async Task SaveDraftAsync()
    {
        if(Busy)throw new InvalidOperationException("請等待目前的日曆操作完成。");
        Busy=true;
        try{await calendar.SaveOptionsAsync(Draft());await LoadAsync();Message="日曆更新設定已儲存。";}
        finally{Busy=false;}
    }
    [RelayCommand] private async Task UpdateAsync()
    {
        if(Busy)return;Busy=true;
        try
        {
            if(Draft()!=(await calendar.GetAsync()).Options)throw new InvalidOperationException("請先儲存畫面上的設定，再立即更新。");
            Message="正在下載並驗證兩份 JSON…";var state=await calendar.UpdateAsync(true);Render(state);Message=state.LastResult;
        }
        catch(Exception ex){Message=ex.Message;}finally{Busy=false;}
    }
}
