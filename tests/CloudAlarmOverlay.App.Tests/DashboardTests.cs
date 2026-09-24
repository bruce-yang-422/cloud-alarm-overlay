using System.IO;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class DashboardTests
{
    [Fact]
    public async Task Today_cards_use_scheduled_day_and_next_card_uses_real_task_and_calendar()
    {
        var paths = new Paths();
        using var host = new HostBuilder().ConfigureServices(s =>
        {
            CompositionRoot.ConfigureServices(s);
            s.AddSingleton<IAppPaths>(paths);
        }).Build();
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var today = DateTime.Today;
            var next = today.AddDays(1).AddHours(14);
            await host.Services.GetRequiredService<ITaskService>().SaveLocalAsync(new AlarmTask
            {
                Id = "tomorrow", Title = "明日提醒", Description = "請帶報表", ScheduledAt = next,
                CreatedAt = today, UpdatedAt = today
            });
            await host.Services.GetRequiredService<ILunarCalendarRepository>().ReplaceCacheAsync(
                [new LunarCalendarEntry { Date = DateOnly.FromDateTime(next), LunarDay = 15, LunarDate = "八月十五" }]);
            var logs = host.Services.GetRequiredService<IAckLogRepository>();
            var done = new AcknowledgementLog
            {
                Id = "done", TaskId = "past", DeviceId = "test", ScheduledAt = today,
                TriggeredAt = today, AcknowledgedAt = today.AddMinutes(1), Result = "Acknowledged"
            };
            await logs.SaveAsync(done);
            await logs.SaveAsync(done with { Id = "unacked", AcknowledgedAt = null, Result = "Overdue_Unacked" });
            // Yesterday's task acknowledged today belongs to yesterday's scheduled cohort.
            await logs.SaveAsync(done with { Id = "yesterday", ScheduledAt = today.AddDays(-1) });
            var vm = host.Services.GetRequiredService<MainViewModel>();
            await vm.InitializeAsync();
            Assert.Equal(2, vm.TodayCount);
            Assert.Equal(1, vm.AcknowledgedCount);
            Assert.Equal(1, vm.PendingCount);
            Assert.Equal("明日提醒", vm.NextTitle);
            Assert.Equal("請帶報表", vm.NextDescription);
            Assert.Equal("14:00", vm.NextClock);
            Assert.Equal("農曆 "+MainViewModel.LocalLunarDate(next), vm.NextLunar);
            Assert.True(vm.RemainingHours > 0);
            vm.OpenTasksCommand.Execute(null);
            Assert.Equal(1, vm.PageIndex);
            await host.Services.GetRequiredService<ITaskService>().DeleteLocalAsync("tomorrow");
            await vm.RefreshCommand.ExecuteAsync(null);
            Assert.Null(vm.NextReminder);
            Assert.Equal("—", vm.CountdownLabel);
            Assert.Equal(0, vm.RemainingHours);
        }
        finally { if (Directory.Exists(paths.DataDirectory)) Directory.Delete(paths.DataDirectory, true); }
    }

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "CloudAlarmOverlay.DashboardTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
    }
}
