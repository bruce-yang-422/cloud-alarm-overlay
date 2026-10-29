using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.App.Tests;

public sealed class AlarmTaskCompletionTests
{
    private static AlarmTask Task(string level)=>new()
    {
        Id="completion",Title="任務",Level=level,ScheduledAt=DateTime.Today.AddHours(15),
        CreatedAt=DateTime.Today,UpdatedAt=DateTime.Today
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_confirmation_and_task_completion_are_distinct(bool complete)
    {
        var vm=new AlarmViewModel(Task(AlarmLevels.High),"123-456-789",false,allowCompletion:true);
        var confirmed=false;vm.Confirmed+=()=>confirmed=true;
        Assert.Equal("只確認已讀",vm.ConfirmLabel);
        if(complete)vm.CompleteTaskCommand.Execute(null);else vm.ConfirmCommand.Execute(null);
        Assert.True(confirmed);
        Assert.Equal(complete,vm.TaskCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_maximum_level_actions_require_the_confirmation_code(bool complete)
    {
        var vm=new AlarmViewModel(Task(AlarmLevels.Max),"123-456-789",false,allowCompletion:true);
        var confirmed=false;vm.Confirmed+=()=>confirmed=true;
        var command=complete?vm.CompleteTaskCommand:vm.ConfirmCommand;
        vm.Input="incorrect";command.Execute(null);
        Assert.False(confirmed);Assert.False(vm.TaskCompleted);Assert.NotEmpty(vm.Error);
        vm.Input="123456789";command.Execute(null);
        Assert.True(confirmed);Assert.Equal(complete,vm.TaskCompleted);
    }

    [Theory]
    [InlineData(true,null)]
    [InlineData(false,"健康工具")]
    [InlineData(false,"番茄鐘")]
    public void Preview_and_special_notifications_do_not_complete_tasks(bool preview,string? caption)
    {
        var vm=new AlarmViewModel(Task(AlarmLevels.High),"",preview,caption,allowCompletion:true);
        var confirmed=false;vm.Confirmed+=()=>confirmed=true;
        Assert.False(vm.CanComplete);Assert.False(vm.CompleteTaskCommand.CanExecute(null));
        vm.CompleteTaskCommand.Execute(null);
        Assert.False(confirmed);Assert.False(vm.TaskCompleted);
        Assert.False(new AlarmViewModel(Task(AlarmLevels.High),"",false).CanComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public System.Threading.Tasks.Task Task_notification_shows_both_actions_and_returns_the_selected_result(bool complete)
        =>MilestoneOneTests.RunSta(async()=>
        {
            var vm=new AlarmViewModel(Task(AlarmLevels.High),"",false,allowCompletion:true);
            var window=new AlarmWindow(vm,AlarmLevels.High){ShowActivated=false};
            try
            {
                window.Show();window.UpdateLayout();
                var read=(Button)window.FindName("ConfirmButton");
                var done=(Button)window.FindName("CompleteTaskButton");
                Assert.True(read.IsVisible);Assert.True(done.IsVisible);
                Assert.Equal("只確認已讀",read.Content);Assert.Equal("已完成",done.Content);
                var chosen=complete?done:read;
                chosen.Command.Execute(null);
                Assert.True(await window.Completion);Assert.Equal(complete,vm.TaskCompleted);
            }
            finally{if(window.IsVisible)window.Finish(false);}
        });
}
