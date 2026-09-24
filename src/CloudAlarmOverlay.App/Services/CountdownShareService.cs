using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.Services;
public sealed class CountdownShareService(ICountdownRepository countdowns,ILunarCalendarRepository lunar,IHolidayRepository holidays,
    ISettingsRepository settings,IThemeService theme,TimeProvider clock,CountdownShareRenderer renderer)
{
    public async Task ShowAsync(string id)
    {
        var item=(await countdowns.GetAllAsync()).SingleOrDefault(i=>i.Id==id)
            ?? throw new InvalidOperationException("此倒數項目已刪除，請重新整理清單。");
        var lunarDays=(await lunar.GetAllAsync()).ToDictionary(d=>d.Date,d=>d.LunarDay);
        var holidayList=await holidays.GetAllAsync();
        var branding=(await settings.GetAsync(CountdownShareSnapshot.BrandingSettingKey))?.Value!="false";
        var snapshot=CountdownShareSnapshot.Create(item,clock.GetLocalNow().DateTime,lunarDays,holidayList);
        var vm=new CountdownShareViewModel(snapshot,renderer,theme.IsDark,theme.ColorStyle,branding);
        new CountdownShareWindow(vm){Owner=Application.Current?.MainWindow}.ShowDialog();
    }
}
