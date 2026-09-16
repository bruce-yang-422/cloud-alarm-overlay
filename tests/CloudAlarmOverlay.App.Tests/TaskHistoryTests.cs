using System.IO;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;
public sealed class TaskHistoryTests
{
    [Fact] public async Task Combined_filters_statistics_paging_export_and_reset_follow_the_same_result_set()
    {
        var paths=new Paths();var dialogs=new Dialogs();
        using var host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);s.AddSingleton<IUserDialogs>(dialogs);}).Build();
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var repo=host.Services.GetRequiredService<IAckLogRepository>();var today=DateTime.Today;
            for(var i=0;i<32;i++)await repo.SaveAsync(new(){Id=i.ToString(),TaskId="removed",TaskName="每日 Team 會議 "+i,DeviceId="local",Source="SheetA",ScheduledAt=today,TriggeredAt=today.AddMinutes(i),AcknowledgedAt=today.AddMinutes(i+1),DurationSeconds=60,Result="Acknowledged"});
            await repo.SaveAsync(new(){Id="late",TaskId="removed",TaskName="其他會議",DeviceId="local",Source=TaskSources.Local,ScheduledAt=today,TriggeredAt=today.AddHours(2),Result="Overdue_Unacked"});
            await repo.SaveAsync(new(){Id="literal",TaskId="removed",TaskName="版本 a+b.[1] 確認",DeviceId="local",Source="SheetB",ScheduledAt=today,TriggeredAt=today.AddHours(3),Result="NotLaunched"});
            await repo.SaveAsync(new(){Id="old",TaskId="removed",TaskName="每日 Team 會議 old",DeviceId="local",Source="SheetA",ScheduledAt=today.AddDays(-31),TriggeredAt=today.AddDays(-31),Result="Acknowledged"});
            var vm=host.Services.GetRequiredService<MainViewModel>();await vm.InitializeAsync();
            Assert.Equal(15,vm.History.Count);Assert.Equal(32,vm.OnTimeHistoryCount);Assert.Equal(1,vm.UnackedHistoryCount);Assert.Equal(1,vm.NotLaunchedHistoryCount);
            vm.HistorySearch="team 每日";vm.HistorySource="SheetA";vm.HistoryResult="準時簽收";vm.HistoryFrom=today;vm.HistoryTo=today;
            Assert.Equal(32,vm.OnTimeHistoryCount);Assert.Equal(0,vm.NotLaunchedHistoryCount);
            vm.NextHistoryCommand.Execute(null);Assert.Equal(2,vm.HistoryPage);
            await vm.ExportHistoryCommand.ExecuteAsync(null);
            Assert.Equal(34,dialogs.Csv.Split("\r\n").Length);Assert.StartsWith("AckLog_",dialogs.Filename);Assert.DoesNotContain("old",dialogs.Csv);
            vm.NextHistoryCommand.Execute(null);Assert.Equal(2,vm.History.Count);Assert.False(vm.CanNextHistory);
            vm.HistorySearch="每日%31";Assert.Single(vm.History);Assert.Equal(1,vm.HistoryPage);
            vm.HistorySearch="每%確認";Assert.Empty(vm.History);
            vm.ClearHistoryFiltersCommand.Execute(null);vm.HistorySearch="a+b.[1]";Assert.Single(vm.History);
            vm.HistoryFrom=today.AddDays(1);Assert.Empty(vm.History);Assert.NotEmpty(vm.HistoryHint);Assert.False(vm.CanNextHistory);
            vm.ClearHistoryFiltersCommand.Execute(null);
            Assert.Equal(today.AddDays(-29),vm.HistoryFrom);Assert.Equal("全部",vm.HistorySource);Assert.Equal("",vm.HistorySearch);
            Assert.Equal(32,vm.OnTimeHistoryCount);Assert.Empty(vm.Pomodoro.History);
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
    }
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmHistoryTests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
    private sealed class Dialogs:IUserDialogs
    {
        public string Csv="",Filename="";
        public bool Confirm(string message)=>true;
        public void Edit(AlarmTask? task,bool copy){}
        public void Export(string contents)=>Csv=contents;
        public void ExportNamed(string contents,string filename){Csv=contents;Filename=filename;}
    }
}