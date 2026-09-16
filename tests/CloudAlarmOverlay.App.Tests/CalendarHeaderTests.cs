using System.IO;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;
public sealed class CalendarHeaderTests
{
    [Fact] public async Task Header_uses_cache_with_offline_lunar_fallback_and_rolls_over_at_midnight()
    {
        var paths=new Paths();
        using var host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);}).Build();
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var vm=host.Services.GetRequiredService<MainViewModel>();
            Assert.Equal("正月初一",MainViewModel.LocalLunarDate(new(2024,2,10)));
            Assert.Equal("正月二十",MainViewModel.LocalLunarDate(new(2024,2,29)));
            await vm.RefreshCalendarAsync(new(2024,2,10,23,59,59));
            Assert.Equal("正月初一",vm.LunarToday);Assert.Equal("23:59:59",vm.CurrentClock);
            Assert.Equal("尚無節氣資料",vm.CurrentSolarTerm);
            await host.Services.GetRequiredService<ILunarCalendarRepository>().ReplaceCacheAsync([
                new(){Date=new(2024,2,4),LunarDay=25,SolarTerm="立春"},
                new(){Date=new(2024,2,11),LunarDay=2,LunarDate="正月初二"}]);
            await vm.RefreshCalendarAsync(new(2024,2,11));
            Assert.Equal("正月初二",vm.LunarToday);Assert.Equal("00:00:00",vm.CurrentClock);
            Assert.StartsWith("2024/02/11",vm.SolarDate);Assert.Equal("立春",vm.CurrentSolarTerm);
            vm.UpdateClock(new(2024,2,11,0,0,1));Assert.Equal("00:00:01",vm.CurrentClock);
            await vm.RefreshCalendarAsync(new(2024,4,1));Assert.Equal("尚無節氣資料",vm.CurrentSolarTerm);
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
    }
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmHeaderTests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
}