using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class WeatherViewModel(IWeatherService weather, CloudAlarmOverlay.Core.Repositories.IAdminSettingsStore admin,TimeProvider clock) : ObservableObject
{
    public const string Attribution = "天氣資料來源：Open-Meteo.com（CC BY 4.0）";
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string? selectedCounty;
    [ObservableProperty] private TaiwanWeatherDistrict? selectedDistrict;
    private bool choosingSearchResult;
    [ObservableProperty] private string locationQuery = "";
    [ObservableProperty] private TaiwanWeatherDistrict? selectedSearchResult;
    public ObservableCollection<TaiwanWeatherDistrict> SearchResults { get; } = [];
    public bool HasLocationQuery => !string.IsNullOrWhiteSpace(LocationQuery);
    public string SearchHint => !HasLocationQuery ? "可輸入地名或郵遞區號，例如：板橋、新北 板橋、220。" :
        SearchResults.Count == 0 ? "找不到符合地點。郵遞區號可輸入 3、5 或 6 碼；快選僅使用前 3 碼。" :
        $"找到 {SearchResults.Count} 個地點，請選擇；郵遞區號僅依前 3 碼對應行政區。";
    partial void OnLocationQueryChanged(string value)
    {
        SelectedSearchResult = null; SearchResults.Clear();
        foreach (var district in TaiwanWeatherLocations.Search(value)) SearchResults.Add(district);
        OnPropertyChanged(nameof(HasLocationQuery)); OnPropertyChanged(nameof(SearchHint));
    }
    partial void OnSelectedSearchResultChanged(TaiwanWeatherDistrict? value)
    {
        if (value is null) return;
        choosingSearchResult = true;
        try { SelectedCounty = value.County; SelectedDistrict = value; }
        finally { choosingSearchResult = false; }
    }
    [RelayCommand] private void ChooseUniqueLocation()
    {
        if (SearchResults.Count == 1) SelectedSearchResult = SearchResults[0];
    }
    [RelayCommand] private void ClearLocationQuery() => LocationQuery = "";
    public IReadOnlyList<string> Counties => TaiwanWeatherLocations.Counties;
    public ObservableCollection<TaiwanWeatherDistrict> Districts { get; } = [];
    partial void OnSelectedCountyChanged(string? value)
    {
        if (!choosingSearchResult) SelectedSearchResult = null;
        SelectedDistrict = null; SelectedLocation = null;
        Districts.Clear();
        foreach (var district in TaiwanWeatherLocations.All.Where(d => d.County == value)) Districts.Add(district);
    }
    partial void OnSelectedDistrictChanged(TaiwanWeatherDistrict? value)
    {
        if (!choosingSearchResult && SelectedSearchResult != value) SelectedSearchResult = null;
        SelectedLocation = value?.Location;
        OnPropertyChanged(nameof(SelectedLocationLabel));
    }
    public string SelectedLocationLabel => SelectedDistrict is {} district ? "選取地點：" + district.Location.Name + "　參考位置：" + district.ReferenceName : "請選擇縣市與鄉鎮市區。";
    [ObservableProperty] private WeatherLocation? selectedLocation;
    [ObservableProperty] private string message = "";
    public bool Visible => weather.Options.Enabled;
    public string LocationLabel => weather.EffectiveLocation is {} l ? "目前地點："+l.Name+(weather.Options.Location is null ? "（公司預設）" : "") : "尚未設定天氣地點";
    public string Heading => weather.EffectiveLocation is {} l ? "區域天氣（" + l.Name + "）" : "區域天氣";
    public Uri ForecastUri => new(TaiwanWeatherLocations.All.FirstOrDefault(d => d.Location == weather.EffectiveLocation) is {} district
        ? "https://www.cwa.gov.tw/V8/C/W/Town/Town.html?TID=" + district.CwaTownId
        : "https://www.cwa.gov.tw/V8/C/W/Town/Town.html");
    private bool Available => weather.Snapshot is {} s && clock.GetUtcNow() - s.UpdatedAt < TimeSpan.FromHours(6);
    public bool Stale => weather.IsStale || !Available;
    public string Current => weather.EffectiveLocation is null ? "請至設定 → 天氣選擇地點" : !Available ? "天氣資料暫不可用" :
        $"{WeatherCodes.Describe(weather.Snapshot!.Code).Icon} 現在 {weather.Snapshot.Temperature:0.#}°C　降雨 {weather.Snapshot.Precipitation:0.#} mm";
    public string Tomorrow => !Available ? "" : $"{WeatherCodes.Describe(weather.Snapshot!.Tomorrow.Code).Icon} {ForecastLabel} {weather.Snapshot.Tomorrow.Minimum:0.#}–{weather.Snapshot.Tomorrow.Maximum:0.#}°C　降雨 {weather.Snapshot.Tomorrow.RainProbability}%";
    private string ForecastLabel => weather.Snapshot!.Tomorrow.Date == DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(8)).AddDays(1) ? "明天" : weather.Snapshot.Tomorrow.Date.ToString("MM/dd");
    public string UpdateLabel => weather.Snapshot is {} s && Stale ? $"上次更新 {s.UpdatedAt.LocalDateTime:HH:mm}" : "";
    public string Details => (weather.Snapshot is {} s ? $"{WeatherCodes.Describe(s.Code).Description}，體感 {s.ApparentTemperature:0.#}°C\n明天：{WeatherCodes.Describe(s.Tomorrow.Code).Description}\n預報時間（台北）：{s.ObservedAt:MM/dd HH:mm}\n更新：{s.UpdatedAt.LocalDateTime:MM/dd HH:mm}\n" : "") + Attribution + "\n點擊查看氣象署詳細預報（以預設瀏覽器開啟）";
    public void Tick()
    {
        foreach (var name in new[] { nameof(LocationLabel), nameof(Visible), nameof(Heading), nameof(Current), nameof(Tomorrow), nameof(UpdateLabel), nameof(Details), nameof(Stale) }) OnPropertyChanged(name);
    }
    public async Task LoadAsync()
    {
        await weather.LoadAsync(); Enabled = weather.Options.Enabled;
        SelectedCounty = null; SelectedDistrict = null; SelectedLocation = null;
        Message = ""; LocationQuery = "";
        if (weather.Options.Location is {} l)
        {
            var match = TaiwanWeatherLocations.All.FirstOrDefault(d => d.Location.Name == l.Name);
            if (match is not null) { SelectedCounty = match.County; SelectedDistrict = match; }
            else Message = "已保留原有地點；可選擇縣市及鄉鎮市區後重新儲存。";
            SelectedLocation = match?.Location ?? l;
        }
        Tick();
    }
    [RelayCommand] private async Task SaveAsync()
    {
        try { if (SelectedCounty is not null && SelectedDistrict is null) throw new InvalidOperationException("請選擇鄉鎮市區。"); await weather.SaveAsync(new(Enabled, SelectedLocation)); Tick(); Message = "天氣設定已儲存。"; }
        catch (Exception ex) { Message = ex.Message; }
    }
    [RelayCommand] private async Task SaveCompanyDefaultAsync()
    {
        try
        {
            if(SelectedDistrict is null)throw new InvalidOperationException("請先選擇公司所在縣市及鄉鎮市區。");
            await admin.SaveAsync([new Setting{Key="WeatherDefaultLocation",Value=System.Text.Json.JsonSerializer.Serialize(SelectedLocation),Locked=true}]);
            await weather.SaveAsync(weather.Options); Tick(); Message="公司預設地點已儲存，個人地點仍優先。";
        }
        catch(Exception ex){Message=ex.Message;}
    }
    [RelayCommand] private async Task UseDefaultAsync()
    {
        SelectedCounty = null; SelectedDistrict = null; SelectedLocation = null; await SaveAsync();
    }
}
