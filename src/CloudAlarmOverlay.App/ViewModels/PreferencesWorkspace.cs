using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.ViewModels;

public enum SettingsPage { Sound, Quiet, Appearance, Home, Editing, Google, Updates, Device }
public sealed record SettingsNavigation(SettingsPage Page,string Name,string Group,string Icon);

public partial class PreferencesViewModel
{
    public SettingsNavigation[] Navigation {get;}=[
        new(SettingsPage.Sound,"通知音效","提醒通知","\uE767"),new(SettingsPage.Quiet,"勿擾與顯示","提醒通知","\uE708"),
        new(SettingsPage.Appearance,"外觀與視窗","個人偏好","\uE790"),new(SettingsPage.Home,"首頁與天氣","個人偏好","\uE80F"),new(SettingsPage.Editing,"編輯與分享","個人偏好","\uE70F"),
        new(SettingsPage.Google,"Google 帳號與同步","帳號與應用程式","\uE77B"),new(SettingsPage.Updates,"軟體更新","帳號與應用程式","\uE72C"),new(SettingsPage.Device,"我的裝置","帳號與應用程式","\uE7F4")];
    private SettingsPage selectedPage;
    public Func<bool>? CanLeavePage {get;set;}
    public SettingsPage SelectedPage
    {
        get=>selectedPage;
        set
        {
            if(value==selectedPage||!Enum.IsDefined(value))return;
            if(IsSaving||CanLeavePage?.Invoke()==false){OnPropertyChanged();return;}
            SetProperty(ref selectedPage,value);Message="";EmojiMessage="";HomePinMessage="";CountdownShareMessage="";
            OnPropertyChanged(nameof(IsGoogleSettings));
        }
    }
    public bool IsGoogleSettings=>SelectedPage==SettingsPage.Google;
    public string DeviceDescription=>Maintenance.IsCompanyMode?"裝置識別由 IT 分配，僅供查看。":"此電腦的裝置識別，僅供查看。";
    public bool IsSaving {get;private set;}
    public event Action? DraftsChanged;
    private int savedFlash=500,savedPinLimit=2;
    private string savedQuiet="[]",savedEmojis="";
    private bool savedBranding=true;
    private WeatherDraft? savedWeather;
    private sealed record WeatherDraft(bool Enabled,string? County,TaiwanWeatherDistrict? District,WeatherLocation? Location);
    private WeatherDraft? WeatherState()=>Weather is {} w?new(w.Enabled,w.SelectedCounty,w.SelectedDistrict,w.SelectedLocation):null;
    private string QuietState()=>JsonSerializer.Serialize(QuietPeriods.Select(p=>new QuietPeriod(p.Start,p.End)));
    private string EmojiState()=>string.Join("\n",EmojiItems);
    private void AcceptAllDrafts()
    {
        savedFlash=FlashMilliseconds;savedQuiet=QuietState();savedPinLimit=HomePinLimit;
        savedEmojis=EmojiState();savedBranding=CountdownShareBranding;savedWeather=WeatherState();DraftsChanged?.Invoke();
    }
    public bool IsPageDirty(SettingsPage page)=>page switch
    {
        SettingsPage.Quiet=>(CanEditFlash&&FlashMilliseconds!=savedFlash)||(CanEditQuiet&&QuietState()!=savedQuiet),
        SettingsPage.Home=>HomePinLimit!=savedPinLimit||WeatherState()!=savedWeather,
        SettingsPage.Editing=>EmojiState()!=savedEmojis||CountdownShareBranding!=savedBranding,
        _=>false
    };
    public void DiscardPage(SettingsPage page)
    {
        switch(page)
        {
            case SettingsPage.Quiet:
                FlashMilliseconds=savedFlash;QuietPeriods.Clear();
                foreach(var p in JsonSerializer.Deserialize<QuietPeriod[]>(savedQuiet)??[])QuietPeriods.Add(new(){Start=p.Start,End=p.End});break;
            case SettingsPage.Home:
                HomePinLimit=savedPinLimit;
                if(Weather is {} w&&savedWeather is {} s){w.Enabled=s.Enabled;w.SelectedCounty=s.County;w.SelectedDistrict=s.District;w.SelectedLocation=s.Location;w.LocationQuery="";}break;
            case SettingsPage.Editing:SetEmojiItems(savedEmojis.Split('\n',StringSplitOptions.RemoveEmptyEntries));CountdownShareBranding=savedBranding;EmojiMessage="";break;
        }
        DraftsChanged?.Invoke();
    }
    public async Task SavePageAsync()
    {
        if(IsSaving)return;
        IsSaving=true;DraftsChanged?.Invoke();var completed=new List<string>();
        try
        {
            // Validate every group before writing; accept each successful group separately so
            // a later failure never causes a retry/discard to misrepresent persisted values.
            switch(SelectedPage)
            {
                case SettingsPage.Quiet:
                    var quiet=new Setting{Key="QuietPeriods",Value=QuietState()};
                    var flash=new Setting{Key="FlashMilliseconds",Value=FlashMilliseconds.ToString()};
                    if(CanEditQuiet)NotificationPreferences.Validate(quiet);
                    if(CanEditFlash)NotificationPreferences.Validate(flash);
                    if(CanEditQuiet&&quiet.Value!=savedQuiet){await settings.SaveAsync(quiet);savedQuiet=quiet.Value;completed.Add("靜音時段");}
                    if(CanEditFlash&&FlashMilliseconds!=savedFlash){await settings.SaveAsync(flash);savedFlash=FlashMilliseconds;completed.Add("閃爍間隔");}break;
                case SettingsPage.Home:
                    if(!HomePinLimitChoices.Contains(HomePinLimit))throw new ArgumentException("釘選上限需為 2～5 張。");
                    Weather?.ValidateDraft();
                    if(HomePinLimit!=savedPinLimit){await settings.SaveAsync(new(){Key=HomePinOptions.SettingKey,Value=HomePinLimit.ToString()});savedPinLimit=HomePinLimit;changes.Notify();completed.Add("釘選上限");}
                    if(Weather is {} weather&&WeatherState()!=savedWeather){await weather.SaveDraftAsync();savedWeather=WeatherState();completed.Add("天氣");}break;
                case SettingsPage.Editing:
                    Services.EmojiLibrary.Parse(EmojiState());
                    if(EmojiState()!=savedEmojis){await emojis.SaveAsync(EmojiState());savedEmojis=EmojiState();completed.Add("常用 emoji");}
                    if(CountdownShareBranding!=savedBranding){await settings.SaveAsync(new(){Key=CountdownShareSnapshot.BrandingSettingKey,Value=CountdownShareBranding?"true":"false"});savedBranding=CountdownShareBranding;completed.Add("分享圖片");}break;
            }
            Message="變更已儲存。";EmojiMessage="";
        }
        catch(Exception ex){Message=(completed.Count>0?$"已儲存：{string.Join("、",completed)}。其餘變更尚未儲存：":"尚未儲存：")+ex.Message;}
        finally{IsSaving=false;DraftsChanged?.Invoke();}
    }
}
