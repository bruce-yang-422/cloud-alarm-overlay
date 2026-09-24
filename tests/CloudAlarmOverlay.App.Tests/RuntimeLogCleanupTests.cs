using System.IO;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class RuntimeLogCleanupTests
{
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmLogTests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now=new(2026,9,24,12,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>Now;
        public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.Utc;
    }
    [Fact] public async Task Default_retention_deletes_only_expired_runtime_files_and_retries_locked_files()
    {
        var paths=new Paths();var clock=new Clock();
        using var host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);s.AddSingleton<TimeProvider>(clock);}).Build();
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var folder=Path.Combine(paths.DataDirectory,"logs");Directory.CreateDirectory(folder);
            var keep=new[]{"runtime-20260826.log","runtime-20260924.log","runtime-20260925.log","runtime-20260230.log","runtime-20260801.log.bak","other-20260101.log","backup.calbak"};
            foreach(var name in keep)File.WriteAllText(Path.Combine(folder,name),"keep");
            File.WriteAllText(Path.Combine(folder,"runtime-20260825.log"),"old");
            var lockedPath=Path.Combine(folder,"runtime-20260824.log");File.WriteAllText(lockedPath,"locked");
            Directory.CreateDirectory(Path.Combine(folder,"nested"));File.WriteAllText(Path.Combine(folder,"nested","runtime-20260101.log"),"keep");
            var cleanup=host.Services.GetRequiredService<RuntimeLogCleanup>();
            using(var locked=new FileStream(lockedPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                Assert.Equal(1,await cleanup.CleanupAsync());Assert.True(File.Exists(lockedPath));
            }
            Assert.Equal(1,await cleanup.CleanupAsync());
            foreach(var name in keep)Assert.True(File.Exists(Path.Combine(folder,name)),name);
            Assert.True(File.Exists(Path.Combine(folder,"nested","runtime-20260101.log")));
            Assert.True(File.Exists(paths.DatabasePath));
            Assert.Equal(0,await cleanup.CleanupAsync());
            clock.Now=clock.Now.AddDays(1);
            Assert.Equal(1,await cleanup.CleanupAsync()); // Yesterday's boundary expires at midnight.
        }
        finally {host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
    }

    [Fact] public async Task Admin_can_change_retention_and_json_roundtrip_preserves_it()
    {
        var paths=new Paths();var clock=new Clock();
        using var host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);s.AddSingleton<TimeProvider>(clock);}).Build();
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var settings=host.Services.GetRequiredService<ISettingsRepository>();
            var admin=host.Services.GetRequiredService<AdminViewModel>();
            admin.LogRetentionDaysText="7";await admin.SaveLogRetentionCommand.ExecuteAsync(null);
            Assert.Null(await settings.GetAsync(LogRetentionPolicy.Key));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>settings.SaveAsync(new(){Key=LogRetentionPolicy.Key,Value="7"}));
            var auth=host.Services.GetRequiredService<IAuthenticationService>();await auth.EnsureDefaultAdministratorAsync();Assert.True(await auth.AuthenticateAsync("admin","12345"));
            await admin.LoadAsync();Assert.Equal("30",admin.LogRetentionDaysText);
            admin.LogRetentionDaysText="7";await admin.SaveLogRetentionCommand.ExecuteAsync(null);
            Assert.Equal("7",(await settings.GetAsync(LogRetentionPolicy.Key))!.Value);
            admin.LogRetentionDaysText="0";await admin.SaveLogRetentionCommand.ExecuteAsync(null);
            Assert.Equal("7",(await settings.GetAsync(LogRetentionPolicy.Key))!.Value);
            var parsed=AdminSettingsJson.Parse(await AdminSettingsJson.ExportAsync(settings));
            Assert.Equal("7",parsed.Single(x=>x.Key==LogRetentionPolicy.Key).Value);
            var folder=Path.Combine(paths.DataDirectory,"logs");Directory.CreateDirectory(folder);
            foreach(var date in new[]{"20260917","20260918","20260924"})File.WriteAllText(Path.Combine(folder,$"runtime-{date}.log"),"test");
            Assert.Equal(1,await host.Services.GetRequiredService<RuntimeLogCleanup>().CleanupAsync());
            Assert.True(File.Exists(Path.Combine(folder,"runtime-20260918.log")));
            await host.Services.GetRequiredService<IAdminSettingsStore>().SaveAsync(AdminSettingsJson.Parse("{\"FormatVersion\":1,\"RuntimeLogRetentionDays\":1}"));
            Assert.Equal(1,await host.Services.GetRequiredService<RuntimeLogCleanup>().CleanupAsync());
            Assert.True(File.Exists(Path.Combine(folder,"runtime-20260924.log")));
        }
        finally {host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
    }
    [Theory][InlineData("0")][InlineData("366")][InlineData("-1")][InlineData("1.5")][InlineData("abc")]
    public void Invalid_values_are_rejected_and_legacy_corruption_falls_back_to_30(string value)
    {
        Assert.Throws<ArgumentException>(()=>LogRetentionPolicy.Validate(value));
        Assert.Equal(30,LogRetentionPolicy.Read(value));
    }
}
