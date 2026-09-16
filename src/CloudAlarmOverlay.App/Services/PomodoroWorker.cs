using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace CloudAlarmOverlay.App.Services;
public sealed class PomodoroWorker(IPomodoroService timer,ISoundService sound,ILogger<PomodoroWorker> logger,NotificationPreferences preferences):BackgroundService
{
    private AlarmWindow? notification;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await timer.InitializeAsync(stoppingToken);

        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.TickAsync(stoppingToken);
                if(Application.Current is {} app)
                {
                    var mode=timer.State.Status=="AwaitingConfirmation"?await preferences.ColorModeAsync(stoppingToken):"亮色";
                    var scheme=timer.State.Status=="AwaitingConfirmation"?await preferences.ColorSchemeAsync(stoppingToken):"依提醒等級";
                    await app.Dispatcher.InvokeAsync(()=>UpdateNotification(stoppingToken,mode,scheme));
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"番茄鐘更新失敗");}
            await Task.Delay(250,stoppingToken);
        }
    }
    private void UpdateNotification(CancellationToken ct,string mode,string scheme)
    {
        if(timer.State.Status!="AwaitingConfirmation"){notification?.Finish(false);notification=null;return;}
        if(notification is not null)return;
        var focus=timer.State.Phase=="Focus";
        var now=DateTime.Now;
        var task=new AlarmTask{Id="pomodoro",Title=focus?"專注時間結束，休息一下吧":"休息結束，準備開始下一輪專注",
            Description=focus?"按下確認後開始休息。":"按下確認返回待命；準備好時再開始下一輪。",ScheduledAt=now,CreatedAt=now,UpdatedAt=now};
        var vm=new AlarmViewModel(task,"",false,"番茄鐘");
        notification=new AlarmWindow(vm,AlarmLevels.Mid,colorMode:mode,colorScheme:scheme);
        var window=notification;
        window.Show();
        _=AcknowledgeAsync(window,ct);
        if(timer.Options.SoundEnabled)_=PlayAsync(ct);
    }
    private async Task PlayAsync(CancellationToken ct)
    {try{await sound.PlayAsync(timer.Options.SoundName,ct);}catch(Exception ex){logger.LogWarning(ex,"番茄鐘音效播放失敗");}}
    private async Task AcknowledgeAsync(AlarmWindow window,CancellationToken ct)
    {
        try{if(await window.Completion && !ct.IsCancellationRequested)await timer.ConfirmAsync(ct);}
        catch(Exception ex){logger.LogError(ex,"番茄鐘確認失敗");}
        finally{if(ReferenceEquals(notification,window))notification=null;}
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if(Application.Current is {} app)await app.Dispatcher.InvokeAsync(()=>{notification?.Finish(false);notification=null;});
        await timer.ResetAsync(cancellationToken);
    }
}