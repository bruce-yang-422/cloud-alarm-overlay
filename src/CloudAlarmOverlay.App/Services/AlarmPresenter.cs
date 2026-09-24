using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Services;
public sealed class AlarmPresenter(IRuntimeStore runtime,IDeviceIdentityService identity,IAckCodeGenerator codes,ChangeSignal changes,
    IHostApplicationLifetime lifetime,NotificationPreferences preferences,ISoundService sound,CloudAlarmOverlay.Core.Repositories.ISystemEventStore events,ISnoozeStore snoozes,SnoozeService snoozeService):IAlarmPresenter,IDisposable
{
    private readonly SemaphoreSlim largeGate=new(1,1);
    private int smallCount;
    private int blockingCount;
    public bool HasBlockingNotification=>Volatile.Read(ref blockingCount)>0;
    public async Task ShowAsync(string occurrenceId,AlarmTask task,DateTime scheduledAt,bool preview,CancellationToken ct=default)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.ApplicationStopping);
        ct=linked.Token;
        SnoozeTicket? ticket=null;
        while(true)
        {
            var minutes=await ShowAttemptAsync(occurrenceId,task,scheduledAt,preview,ct,ticket);
            if(minutes is null || preview)return;
            ticket=await snoozeService.DeferAsync(occurrenceId,task,scheduledAt,minutes.Value,ct);
            if(ticket is null || !await snoozeService.WaitAsync(ticket,ct))return;
        }
    }
    private async Task<int?> ShowAttemptAsync(string occurrenceId,AlarmTask task,DateTime scheduledAt,bool preview,CancellationToken ct,SnoozeTicket? ticket)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.ApplicationStopping);
        ct=linked.Token;
        var device=await identity.GetLocalAsync(ct).ConfigureAwait(false);
        bool large=task.Level is AlarmLevels.High or AlarmLevels.Max;
        if(large)Interlocked.Increment(ref blockingCount);
        bool entered=false;
        AlarmWindow? window=null;
        try
        {
            if(large){await largeGate.WaitAsync(ct).ConfigureAwait(false);entered=true;}
            if(ticket is not null && !await snoozeService.ValidateAsync(ticket,ct))return null;
            var dispatcher=Application.Current.Dispatcher;
            if(!preview && device is null)throw new InvalidOperationException("尚未設定装置識別。");
            if(!preview)await runtime.DisplayedAsync(occurrenceId,task,scheduledAt,device!,ct).ConfigureAwait(false);
            var count=preview?0:await snoozes.CountAsync(occurrenceId,ct);
            var allowSnooze=SnoozePolicy.CanDefer(task.Level,count,await preferences.AllowUrgentSnoozeAsync(ct)) && !task.Id.StartsWith("countdown:",StringComparison.Ordinal);
            var flashMs=await preferences.FlashMillisecondsAsync(ct).ConfigureAwait(false);
            var colorMode=await preferences.ColorModeAsync(ct).ConfigureAwait(false);
            var colorScheme=await preferences.ColorSchemeAsync(ct).ConfigureAwait(false);
            var audio=await preferences.SoundAsync(task.Level,ct).ConfigureAwait(false);
            await dispatcher.InvokeAsync(()=>{
                ct.ThrowIfCancellationRequested();
                window=new AlarmWindow(new AlarmViewModel(task,codes.Generate(),preview,allowSnooze:allowSnooze,snoozeCount:count),task.Level,large?0:smallCount++,flashMs,colorMode,colorScheme);
                window.Show();
                if(audio.Enabled&&audio.Name!="")_ = PlaySafelyAsync(audio.Name);
            });
            changes.Notify();
            using var registration=ct.Register(()=>dispatcher.BeginInvoke(()=>window?.Finish(false)));
            if(await window!.Completion.ConfigureAwait(false) && !preview)
                await runtime.CompleteAsync(occurrenceId,CancellationToken.None).ConfigureAwait(false);
            changes.Notify();
            return window.SnoozeMinutes;
        }
        finally
        {
            if(window is not null&&!large)await Application.Current.Dispatcher.InvokeAsync(()=>smallCount--);
            if(entered)largeGate.Release();
            if(large)Interlocked.Decrement(ref blockingCount);
        }
    }
    private async Task PlaySafelyAsync(string name)
    {
        try{await sound.PlayAsync(name);}
        catch(Exception ex){await events.AppendAsync(new(){EventType="SoundError",Message=ex.Message});}
    }
    public void Dispose()=>largeGate.Dispose();
}
